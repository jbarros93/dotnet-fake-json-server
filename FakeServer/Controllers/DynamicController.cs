﻿using FakeServer.Common;
using JsonFlatFileDataStore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;

namespace FakeServer.Controllers
{
    [Authorize]
    [Route(Config.ApiRoute)]
    public class DynamicController : Controller
    {
        private readonly IDataStore _ds;
        private readonly ApiSettings _settings;

        public DynamicController(IDataStore ds, IOptions<ApiSettings> settings)
        {
            _ds = ds;
            _settings = settings.Value;
        }

        /// <summary>
        /// List collections
        /// </summary>
        /// <returns>List of collections</returns>
        [HttpGet]
        [ResponseCache(CacheProfileName = "Default")]
        public IEnumerable<string> GetCollections()
        {
            return _ds.ListCollections();
        }

        /// <summary>
        /// Replace database.json content
        /// </summary>
        /// <param name="value">New JSON content</param>
        [HttpPost]
        public void UpdateAllData([FromBody]string value)
        {
            _ds.UpdateAll(value);
        }

        /// <summary>
        /// Get items
        /// </summary>
        /// <remarks>
        /// Add filtering with query parameters. E.q. /api/users?age=22&amp;name=Phil (not possible with Swagger)
        ///
        /// Optional parameter names skip/take and offset/limit:
        /// /api/users?skip=10&amp;take=20
        /// /api/users?offset=10&amp;limit=20
        /// </remarks>
        /// <param name="collectionId">Collection id</param>
        /// <param name="skip">Items to skip (optional name offset)</param>
        /// <param name="take">Items to take (optional name limit)</param>
        /// <returns>List of items</returns>
        /// <response code="200">Collection item array</response>
        /// <response code="400">Invalid query parameters</response>
        /// <response code="404">Collection not found</response>
        [HttpGet("{collectionId}")]
        [ResponseCache(CacheProfileName = "Default")]
        public IActionResult GetItems(string collectionId, int skip = 0, int take = 512)
        {
            var collection = _ds.GetCollection(collectionId);

            // Collection can actually just be empty, but in this case we handle it as it is not found
            if (!collection.AsQueryable().Any())
                return NotFound();

            var options = GetQueryOptions(Request.Query, skip, take);

            if (!options.Validate())
                return BadRequest();

            var datas = options.IsTextSearch ? collection.Find(Request.Query["q"]) : collection.AsQueryable();

            foreach (var key in options.QueryParams)
            {
                string propertyName = key;
                Func<dynamic, dynamic, bool> compareFunc = ObjectHelper.Funcs[""];

                var idx = key.LastIndexOf("_");

                if (idx != -1)
                {
                    var op = key.Substring(idx);
                    compareFunc = ObjectHelper.Funcs[op];
                    propertyName = key.Replace(op, "");
                }

                datas = datas.Where(d => ObjectHelper.GetPropertyAndCompare(d as ExpandoObject, propertyName, Request.Query[key], compareFunc));
            }

            var totalCount = datas.Count();

            var paginationHeader = QueryHelper.GetPaginationHeader($"{Request.Scheme}://{Request.Host.Value}{Request.Path}", totalCount, options.Skip, options.Take, options.SkipWord, options.TakeWord);

            var results = datas.Skip(options.Skip).Take(options.Take);

            if (options.Fields.Any())
            {
                results = ObjectHelper.SelectFields(results, options.Fields);
            }

            if (_settings.UseResultObject)
            {
                return Ok(QueryHelper.GetResultObject(results, totalCount, paginationHeader, options));
            }
            else
            {
                Response.Headers.Add("X-Total-Count", totalCount.ToString());
                Response.Headers.Add("Link", QueryHelper.GetHeaderLink(paginationHeader));
                return Ok(results);
            }
        }

        /// <summary>
        /// Get single item
        /// </summary>
        /// <param name="collectionId">Collection id</param>
        /// <param name="id">Item id</param>
        /// <returns>Item</returns>
        /// <response code="200">Item found</response>
        /// <response code="404">Item not found</response>
        [HttpGet("{collectionId}/{id}")]
        [ResponseCache(CacheProfileName = "Default")]
        public IActionResult GetItem(string collectionId, [FromRoute][DynamicBinder]dynamic id)
        {
            var result = _ds.GetCollection(collectionId).Find(e => e.id == id).FirstOrDefault();

            if (result == null)
                return NotFound();

            return Ok(result);
        }

        /// <summary>
        /// Get nested item
        /// </summary>
        /// <remarks>
        /// Add full path separated with periods. E.q. /api/users/1/parents/0/work
        /// </remarks>
        /// <param name="collectionId">Collection id</param>
        /// <param name="id">Item id</param>
        /// <param name="path">Rest of the path</param>
        /// <returns></returns>
        /// <response code="200">Nested item found</response>
        /// <response code="400">Parent item not found</response>
        /// <response code="404">Nested item not found</response>
        [HttpGet("{collectionId}/{id}/{*path}")]
        [ResponseCache(CacheProfileName = "Default")]
        public IActionResult GetNested(string collectionId, [FromRoute][DynamicBinder]dynamic id, string path)
        {
            var item = _ds.GetCollection(collectionId).AsQueryable().FirstOrDefault(e => e.id == id);

            if (item == null)
                return BadRequest();

            var nested = ObjectHelper.GetNestedProperty(item, path);

            if (nested == null)
                return NotFound();

            return Ok(nested);
        }

        /// <summary>
        /// Add new item
        /// </summary>
        /// <param name="collectionId">Collection id</param>
        /// <param name="item">Item to add</param>
        /// <returns>Created item id</returns>
        /// <response code="201">Item created</response>
        /// <response code="400">Item is null</response>
        [HttpPost("{collectionId}")]
        public async Task<IActionResult> AddNewItem(string collectionId, [FromBody]JToken item)
        {
            if (item == null)
                return BadRequest();

            var collection = _ds.GetCollection(collectionId);

            await collection.InsertOneAsync(item);

            return Created($"{Request.Scheme}://{Request.Host.Value}/api/{collectionId}/{item["id"]}", new { id = item["id"] });
        }

        /// <summary>
        /// Replace item from collection
        /// </summary>
        /// <param name="collectionId">Collection id</param>
        /// <param name="id">Id of the item to be replaced</param>
        /// <param name="item">Item's new content</param>
        /// <returns></returns>
        /// <response code="204">Item found and replaced</response>
        /// <response code="400">Item is null</response>
        /// <response code="404">Item not found</response>
        [HttpPut("{collectionId}/{id}")]
        public async Task<IActionResult> ReplaceItem(string collectionId, [FromRoute][DynamicBinder]dynamic id, [FromBody]dynamic item)
        {
            if (item == null)
                return BadRequest();

            // Make sure that new data has id field correctly
            item.id = id;

            var success = await _ds.GetCollection(collectionId).ReplaceOneAsync((Predicate<dynamic>)(e => e.id == id), item, _settings.UpsertOnPut);

            if (success)
                return NoContent();
            else
                return NotFound();
        }

        /// <summary>
        /// Update item's content
        /// </summary>
        /// <remarks>
        /// Patch data contains fields to be updated.
        ///
        ///     {
        ///        "stringField": "some value",
        ///        "intField": 22,
        ///        "boolField": true
        ///     }
        /// </remarks>
        /// <param name="collectionId"></param>
        /// <param name="id">Id of the item to be updated</param>
        /// <param name="patchData">Patch data</param>
        /// <returns></returns>
        /// <response code="204">Item found and updated</response>
        /// <response code="400">Patch data is empty</response>
        /// <response code="404">Item not found</response>
        [HttpPatch("{collectionId}/{id}")]
        public async Task<IActionResult> UpdateItem(string collectionId, [FromRoute][DynamicBinder]dynamic id, [FromBody]JToken patchData)
        {
            dynamic sourceData = JsonConvert.DeserializeObject<ExpandoObject>(patchData.ToString());

            if (!((IDictionary<string, Object>)sourceData).Any())
                return BadRequest();

            var success = await _ds.GetCollection(collectionId).UpdateOneAsync((Predicate<dynamic>)(e => e.id == id), sourceData);

            if (success)
                return NoContent();
            else
                return NotFound();
        }

        /// <summary>
        /// Remove item from collection
        /// </summary>
        /// <param name="collectionId">Collection id</param>
        /// <param name="id">Id of the item to be removed</param>
        /// <returns></returns>
        /// <response code="204">Item found and removed</response>
        /// <response code="404">Item not found</response>
        [HttpDelete("{collectionId}/{id}")]
        public async Task<IActionResult> DeleteItem(string collectionId, [FromRoute][DynamicBinder]dynamic id)
        {
            var success = await _ds.GetCollection(collectionId).DeleteOneAsync(e => e.id == id);

            if (success)
                return NoContent();
            else
                return NotFound();
        }

        private QueryOptions GetQueryOptions(IQueryCollection query, int skip, int take)
        {
            var skipWord = "skip";
            var takeWord = "take";
            var isTextSearch = false;
            var fields = new List<string>();

            var queryParams = query.Keys.ToList();

            if (queryParams.Contains("offset"))
            {
                skip = Convert.ToInt32(query["offset"]);
                queryParams.Remove("offset");
                skipWord = "offset";
            }

            if (queryParams.Contains("limit"))
            {
                take = Convert.ToInt32(query["limit"]);
                queryParams.Remove("limit");
                takeWord = "limit";
            }

            if (queryParams.Contains("q"))
            {
                isTextSearch = true;
                queryParams.Remove("q");
            }

            if (queryParams.Contains("fields"))
            {
                fields = query["fields"].ToString().Split(',').ToList();
                queryParams.Remove("fields");
            }

            queryParams.Remove("skip");
            queryParams.Remove("take");

            return new QueryOptions { Skip = skip, Take = take, SkipWord = skipWord, TakeWord = takeWord, IsTextSearch = isTextSearch, Fields = fields, QueryParams = queryParams };
        }
    }

    public class QueryOptions
    {
        public int Skip { get; set; }

        public int Take { get; set; }

        public string SkipWord { get; set; }

        public string TakeWord { get; set; }

        public bool IsTextSearch { get; set; }

        public List<string> Fields { get; set; }

        public List<string> QueryParams { get; set; }

        public bool Validate()
        {
            if (IsTextSearch && QueryParams.Any())
                return false;

            return true;
        }
    }
}