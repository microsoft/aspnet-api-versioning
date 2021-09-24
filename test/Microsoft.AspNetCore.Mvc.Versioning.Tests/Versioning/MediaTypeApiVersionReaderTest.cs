namespace Microsoft.AspNetCore.Mvc.Versioning
{
    using Extensions.Primitives;
    using FluentAssertions;
    using Http;
    using Moq;
    using Xunit;
    using static System.IO.Stream;

    public class MediaTypeApiVersionReaderTest
    {
        [Fact]
        public void read_should_null_when_media_type_is_unspecified()
        {
            // arrange
            var reader = new MediaTypeApiVersionReader();
            var request = new Mock<HttpRequest>();

            request.SetupGet( r => r.Headers ).Returns( Mock.Of<IHeaderDictionary>() );

            // act
            var version = reader.Read( request.Object );

            // assert
            version.Should().BeNull();
        }

        [Fact]
        public void read_should_retrieve_version_from_content_type()
        {
            // arrange
            var reader = new MediaTypeApiVersionReader();
            var request = new Mock<HttpRequest>();
            var headers = new Mock<IHeaderDictionary>();

            headers.SetupGet( h => h["Content-Type"] ).Returns( new StringValues( "application/json;v=2.0" ) );
            request.SetupGet( r => r.Headers ).Returns( headers.Object );
            request.SetupProperty( r => r.Body, Null );
            request.SetupProperty( r => r.ContentLength, 0L );
            request.SetupProperty( r => r.ContentType, "application/json;v=2.0" );

            // act
            var version = reader.Read( request.Object );

            // assert
            version.Should().Be( "2.0" );
        }

        [Fact]
        public void read_should_retrieve_version_from_accept()
        {
            // arrange
            var reader = new MediaTypeApiVersionReader();
            var request = new Mock<HttpRequest>();
            var headers = new Mock<IHeaderDictionary>();

            headers.SetupGet( h => h["Accept"] ).Returns( new StringValues( "application/json;v=2.0" ) );
            request.SetupGet( r => r.Headers ).Returns( headers.Object );

            // act
            var version = reader.Read( request.Object );

            // assert
            version.Should().Be( "2.0" );
        }

        [Theory]
        [InlineData( new[] { "application/json;q=1;v=2.0" }, "2.0" )]
        [InlineData( new[] { "application/json;q=0.8;v=1.0", "text/plain" }, "1.0" )]
        [InlineData( new[] { "application/json;q=0.5;v=3.0", "application/xml;q=0.5;v=3.0" }, "3.0" )]
        [InlineData( new[] { "application/xml", "application/json;q=0.2;v=1.0" }, "1.0" )]
        [InlineData( new[] { "application/json", "application/xml" }, null )]
        [InlineData( new[] { "application/xml", "application/xml+atom;q=0.8;v=2.5", "application/json;q=0.2;v=1.0" }, "2.5" )]
        public void read_should_retrieve_version_from_accept_with_quality( string[] mediaTypes, string expected )
        {
            // arrange
            var reader = new MediaTypeApiVersionReader();
            var request = new Mock<HttpRequest>();
            var headers = new Mock<IHeaderDictionary>();

            headers.SetupGet( h => h["Accept"] ).Returns( new StringValues( mediaTypes ) );
            request.SetupGet( r => r.Headers ).Returns( headers.Object );

            // act
            var version = reader.Read( request.Object );

            // assert
            version.Should().Be( expected );
        }

        [Fact]
        public void read_should_prefer_version_from_content_type_over_accept()
        {
            // arrange
            var reader = new MediaTypeApiVersionReader();
            var request = new Mock<HttpRequest>();
            var headers = new Mock<IHeaderDictionary>();
            var mediaTypes = new[]
            {
                "application/xml",
                "application/xml+atom;q=0.8;v=1.5",
                "application/json;q=0.2;v=2.0"
            };

            headers.SetupGet( h => h["Accept"] ).Returns( new StringValues( mediaTypes ) );
            headers.SetupGet( h => h["Content-Type"] ).Returns( new StringValues( "application/json;v=2.0" ) );
            request.SetupGet( r => r.Headers ).Returns( headers.Object );
            request.SetupProperty( r => r.Body, Null );
            request.SetupProperty( r => r.ContentLength, 0L );
            request.SetupProperty( r => r.ContentType, "application/json;v=2.0" );
            request.SetupGet( r => r.Headers ).Returns( headers.Object );

            // act
            var version = reader.Read( request.Object );

            // assert
            version.Should().Be( "2.0" );
        }


        [Fact]
        public void read_should_prefer_explicit_version_in_accept_over_implicit_in_content_type()
        {
            // arrange
            var reader = new MediaTypeApiVersionReader();
            var request = new Mock<HttpRequest>();
            var headers = new Mock<IHeaderDictionary>();
            var mediaTypes = new[]
            {
                "application/xml",
                "application/xml+atom;q=0.8;v=1.5",
                "application/json;q=0.2;v=2.0"
            };

            headers.SetupGet( h => h["Accept"] ).Returns( new StringValues( mediaTypes ) );
            headers.SetupGet( h => h["Content-Type"] ).Returns( new StringValues( "application/json" ) );
            request.SetupGet( r => r.Headers ).Returns( headers.Object );
            request.SetupProperty( r => r.Body, Null );
            request.SetupProperty( r => r.ContentLength, 0L );
            request.SetupProperty( r => r.ContentType, "application/json" );
            request.SetupGet( r => r.Headers ).Returns( headers.Object );

            // act
            var version = reader.Read( request.Object );

            // assert
            version.Should().Be( "1.5" );
        }

        [Fact]
        public void read_should_retrieve_version_from_content_type_with_custom_parameter()
        {
            // arrange
            var reader = new MediaTypeApiVersionReader( "version" );
            var request = new Mock<HttpRequest>();
            var headers = new Mock<IHeaderDictionary>();

            headers.SetupGet( h => h["Content-Type"] ).Returns( new StringValues( "application/json;version=1.0" ) );
            request.SetupGet( r => r.Headers ).Returns( headers.Object );
            request.SetupProperty( r => r.Body, Null );
            request.SetupProperty( r => r.ContentLength, 0L );
            request.SetupProperty( r => r.ContentType, "application/json;version=1.0" );

            // act
            var version = reader.Read( request.Object );

            // assert
            version.Should().Be( "1.0" );
        }

        [Fact]
        public void read_should_retrieve_version_from_accept_with_custom_parameter()
        {
            // arrange
            var reader = new MediaTypeApiVersionReader( "version" );
            var request = new Mock<HttpRequest>();
            var headers = new Mock<IHeaderDictionary>();

            headers.SetupGet( h => h["Accept"] ).Returns( new StringValues( "application/json;version=3.0" ) );
            request.SetupGet( r => r.Headers ).Returns( headers.Object );

            // act
            var version = reader.Read( request.Object );

            // assert
            version.Should().Be( "3.0" );
        }
    }
}