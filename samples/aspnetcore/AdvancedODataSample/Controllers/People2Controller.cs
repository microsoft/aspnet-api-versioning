﻿namespace Microsoft.Examples.Controllers
{
    using Microsoft.AspNet.OData;
    using Microsoft.AspNet.OData.Query;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Examples.Models;

    [ApiVersion( "3.0" )]
    [ControllerName( "People" )]
    public class People2Controller : ODataController
    {
        // GET ~/api/people?api-version=3.0
        [HttpGet]
        public IActionResult Get( ODataQueryOptions<Person> options, ApiVersion version ) =>
            Ok( new[] { new Person() { Id = 1, FirstName = "Bill", LastName = "Mei", Email = "bill.mei@somewhere.com", Phone = "555-555-5555" } } );

        // GET ~/api/people/{id}?api-version=3.0
        [HttpGet( "{id}" )]
        public IActionResult Get( int id, ODataQueryOptions<Person> options, ApiVersion version ) =>
            Ok( new Person() { Id = id, FirstName = "Bill", LastName = "Mei", Email = "bill.mei@somewhere.com", Phone = "555-555-5555" } );
    }
}