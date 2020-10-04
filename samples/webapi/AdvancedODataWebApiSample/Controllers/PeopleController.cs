﻿namespace Microsoft.Examples.Controllers
{
    using Microsoft.AspNet.OData;
    using Microsoft.AspNet.OData.Query;
    using Microsoft.AspNet.OData.Routing;
    using Microsoft.Examples.Models;
    using Microsoft.Web.Http;
    using System.Web.Http;

    // note: since the application is configured with AssumeDefaultVersionWhenUnspecified, this controller
    // is resolved without or without an API version, even though it is explicitly versioned
    [ApiVersion( "1.0" )]
    [ApiVersion( "2.0" )]
    [ODataRoutePrefix( "People" )]
    public class PeopleController : ODataController
    {
        // GET ~/api/people
        // GET ~/api/people?api-version=[1.0|2.0]
        [ODataRoute]
        public IHttpActionResult Get( ODataQueryOptions<Person> options, ApiVersion version ) =>
            Ok( new[] { new Person() { Id = 1, FirstName = "Bill", LastName = "Mei", Email = "bill.mei@somewhere.com", Phone = "555-555-5555" } } );

        // GET ~/api/people/{id}
        // GET ~/api/people/{id}?api-version=[1.0|2.0]
        [ODataRoute( "{id}" )]
        public IHttpActionResult Get( int id, ODataQueryOptions<Person> options, ApiVersion version ) =>
            Ok( new Person() { Id = id, FirstName = "Bill", LastName = "Mei", Email = "bill.mei@somewhere.com", Phone = "555-555-5555" } );

        // PATCH ~/api/people/{id}?api-version=2.0
        [MapToApiVersion( "2.0" )]
        [ODataRoute( "{id}" )]
        public IHttpActionResult Patch( int id, Delta<Person> delta, ODataQueryOptions<Person> options, ApiVersion version )
        {
            if ( !ModelState.IsValid )
                return BadRequest( ModelState );

            var person = new Person() { Id = id, FirstName = "Bill", LastName = "Mei", Email = "bill.mei@somewhere.com", Phone = "555-555-5555" };

            delta.Patch( person );

            return Updated( person );
        }
    }
}