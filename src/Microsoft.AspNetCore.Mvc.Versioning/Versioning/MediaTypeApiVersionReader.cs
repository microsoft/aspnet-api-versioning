namespace Microsoft.AspNetCore.Mvc.Versioning
{
    using Microsoft.AspNetCore.Http;
    using System;

    /// <content>
    /// Provides the implementation for ASP.NET Core.
    /// </content>
    [CLSCompliant( false )]
    public partial class MediaTypeApiVersionReader
    {
        /// <summary>
        /// Reads the service API version value from a request.
        /// </summary>
        /// <param name="request">The <see cref="HttpRequest">HTTP request</see> to read the API version from.</param>
        /// <returns>The raw, unparsed service API version value read from the request or <c>null</c> if request does not contain an API version.</returns>
        /// <exception cref="AmbiguousApiVersionException">Multiple, different API versions were requested.</exception>
        public virtual string? Read( HttpRequest request )
        {
            if ( request == null )
            {
                throw new ArgumentNullException( nameof( request ) );
            }

            var headers = request.GetTypedHeaders();
            var contentType = headers.ContentType;
            var contentTypeVersion = contentType != null ? ReadContentTypeHeader( contentType ) : default;

            if ( contentTypeVersion != null )
            {
                return contentTypeVersion;
            }

            var accept = headers.Accept;

            return accept == null ? default : ReadAcceptHeader( accept );
        }
    }
}