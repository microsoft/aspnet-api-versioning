// Copyright (c) .NET Foundation and contributors. All rights reserved.

namespace Asp.Versioning;

#if !NETFRAMEWORK
using System.Buffers;
#endif
using static Asp.Versioning.ApiVersionParameterLocation;

/// <summary>
/// Represents an API version reader that reads the value from a HTTP header.
/// </summary>
public partial class HeaderApiVersionReader : IApiVersionReader
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HeaderApiVersionReader"/> class.
    /// </summary>
    /// <remarks>
    /// There is no default header. Either initialize headers using the <see cref="HeaderNames"/> collection property,
    /// or call a constructor that  accepts header names. Otherwise, this reader has no effect.
    /// </remarks>
    public HeaderApiVersionReader() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="HeaderApiVersionReader"/> class.
    /// </summary>
    /// <param name="headerNames">A <see cref="IEnumerable{T}">sequence</see> of HTTP header names to read the API version from.</param>
    public HeaderApiVersionReader( IEnumerable<string> headerNames ) =>
        HeaderNames.AddRange( headerNames ?? throw new System.ArgumentNullException( nameof( headerNames ) ) );

    /// <summary>
    /// Initializes a new instance of the <see cref="HeaderApiVersionReader"/> class.
    /// </summary>
    /// <param name="headerName">The required HTTP header name to read the API version from.</param>
    /// <param name="otherHeaderNames">An array of other HTTP header names to read the API version from.</param>
    public HeaderApiVersionReader( string headerName, params string[] otherHeaderNames )
    {
        ArgumentException.ThrowIfNullOrEmpty( headerName );

        HeaderNames.Add( headerName );

        if ( otherHeaderNames is not null )
        {
            for ( var i = 0; i < otherHeaderNames.Length; i++ )
            {
                var name = otherHeaderNames[i];

                if ( !string.IsNullOrEmpty( name ) )
                {
                    HeaderNames.Add( name );
                }
            }
        }
    }

    /// <summary>
    /// Gets a collection of HTTP header names that the API version can be read from.
    /// </summary>
    /// <value>A <see cref="ICollection{T}">collection</see> of HTTP header names.</value>
    /// <remarks>HTTP header names are evaluated in a case-insensitive manner.</remarks>
    public ICollection<string> HeaderNames { get; } = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

    /// <summary>
    /// Provides API version parameter descriptions supported by the current reader using the supplied provider.
    /// </summary>
    /// <param name="context">The <see cref="IApiVersionParameterDescriptionContext">context</see> used to add API version parameter descriptions.</param>
    public virtual void AddParameters( IApiVersionParameterDescriptionContext context )
    {
        ArgumentNullException.ThrowIfNull( context );

        var count = HeaderNames.Count;
#if NETFRAMEWORK
        var names = new string[count];
#else
        var pool = ArrayPool<string>.Shared;
        var names = pool.Rent( count );
#endif

        HeaderNames.CopyTo( names, 0 );

        for ( var i = 0; i < count; i++ )
        {
            context.AddParameter( names[i], Header );
        }

#if !NETFRAMEWORK
        pool.Return( names );
#endif
    }
}