﻿namespace given_a_versioned_ApiController
{
    using FluentAssertions;
    using Microsoft.Web;
    using Microsoft.Web.Http.Basic;
    using System.Threading.Tasks;
    using Xunit;
    using static System.Net.HttpStatusCode;

    [Collection( nameof( BasicCollection ) )]
    public class when_a_version_is_mapped_only : AcceptanceTest
    {
        [Fact]
        public async Task then_get_should_return_400()
        {
            // arrange
            var requestUrl = "api/v42/helloworld/unreachable";

            // act
            var response = await GetAsync( requestUrl );

            // assert
            response.StatusCode.Should().Be( BadRequest );
        }

        public when_a_version_is_mapped_only( BasicFixture fixture ) : base( fixture ) { }
    }
}
