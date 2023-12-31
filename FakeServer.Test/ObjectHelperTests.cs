﻿using FakeServer.Common;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using Xunit;

namespace FakeServer.Test
{
    public class ObjectHelperTests
    {
        [Fact]
        public void WebSocketMessage()
        {
            // Anonymous type is generated as internal so we cant test it withou new serialize/deserialize
            // Should add InternalsVisibleTo or just crate a Typed class
            dynamic original = ObjectHelper.GetWebSocketMessage("POST", "/api/humans/2");

            var msg = JsonConvert.DeserializeObject<dynamic>(JsonConvert.SerializeObject(original));

            Assert.Equal("/api/humans/2", msg.Path.Value);
            Assert.Equal("humans", msg.Collection.Value);
            Assert.Equal("2", msg.ItemId.Value);
            Assert.Equal("POST", msg.Method.Value);

            original = ObjectHelper.GetWebSocketMessage("PUT", "api/humans/2/");

            msg = JsonConvert.DeserializeObject<dynamic>(JsonConvert.SerializeObject(original));

            Assert.Equal("api/humans/2/", msg.Path.Value);
            Assert.Equal("humans", msg.Collection.Value);
            Assert.Equal("2", msg.ItemId.Value);
            Assert.Equal("PUT", msg.Method.Value);

            original = ObjectHelper.GetWebSocketMessage("POST", "/api/humans");

            msg = JsonConvert.DeserializeObject<dynamic>(JsonConvert.SerializeObject(original));

            Assert.Equal("/api/humans", msg.Path.Value);
            Assert.Equal("humans", msg.Collection.Value);
            Assert.Equal(null, msg.ItemId.Value);
            Assert.Equal("POST", msg.Method.Value);
        }

        [Fact]
        public void GetValueAsCorrectType()
        {
            Assert.IsType<int>(ObjectHelper.GetValueAsCorrectType("2"));
            Assert.IsType<double>(ObjectHelper.GetValueAsCorrectType("2.1"));
            Assert.IsType<DateTime>(ObjectHelper.GetValueAsCorrectType("7/31/2017"));
            Assert.IsType<string>(ObjectHelper.GetValueAsCorrectType("somevalue"));
        }

        [Fact]
        public void OperatorFunctions()
        {
            Assert.True(ObjectHelper.Funcs[""](2, 2));
            Assert.True(ObjectHelper.Funcs[""]("ok", "ok"));
            Assert.False(ObjectHelper.Funcs[""]("ok2", "ok"));
            Assert.True(ObjectHelper.Funcs["_ne"](3.5, 2.3));
            Assert.True(ObjectHelper.Funcs["_ne"]("ok2", "ok"));
            Assert.True(ObjectHelper.Funcs["_lt"](2, 3));
            Assert.True(ObjectHelper.Funcs["_lt"](2.9999, 3));
            Assert.True(ObjectHelper.Funcs["_lte"](2, 2));
            Assert.True(ObjectHelper.Funcs["_lte"](2.0, 2.0));
            Assert.True(ObjectHelper.Funcs["_gt"](3, 2));
            Assert.False(ObjectHelper.Funcs["_gt"](3.0, 3.1));
            Assert.True(ObjectHelper.Funcs["_gte"](3, 2));
        }

        [Fact]
        public void SelectFields()
        {
            dynamic exp = new ExpandoObject();
            exp.Name = "Jim";
            exp.Age = 20;
            exp.Occupation = "Gardener";

            dynamic exp2 = new ExpandoObject();
            exp2.Name = "Danny";
            exp2.Age = 40;
            exp2.Occupation = "Engineer";

            var result = ObjectHelper.SelectFields(new[] { exp, exp2 }, new[] { "Occupation", "Age" });
            Assert.Equal(2, result.Count());
            Assert.Equal(20, result.ToList()[0]["Age"]);
            Assert.Equal(40, result.ToList()[1]["Age"]);
        }
    }
}