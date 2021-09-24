namespace Microsoft.Web.Http.Versioning
{
    using System;
    using System.Net.Http;

    /// <content>
    /// Provides the implementation for ASP.NET Web API.
    /// </content>
    public partial class MediaTypeApiVersionReader
    {
        /// <summary>
        /// Reads the service API version value from a request.
        /// </summary>
        /// <param name="request">The <see cref="HttpRequestMessage">HTTP request</see> to read the API version from.</param>
        /// <returns>The raw, unparsed service API version value read from the request or <c>null</c> if request does not contain an API version.</returns>
        /// <exception cref="AmbiguousApiVersionException">Multiple, different API versions were requested.</exception>
        public virtual string? Read( HttpRequestMessage request )
        {
            if ( request == null )
            {
                throw new ArgumentNullException( nameof( request ) );
            }

            var contentType = request.Content?.Headers.ContentType;

            var contentTypeVersion = contentType != null ? ReadContentTypeHeader( contentType ) : default;

            if ( contentTypeVersion != null )
            {
                return contentTypeVersion;
            }

            var accept = request.Headers.Accept;

            return accept == null ? default : ReadAcceptHeader( accept );
        }
    }
}