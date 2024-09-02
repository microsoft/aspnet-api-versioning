// Copyright (c) .NET Foundation and contributors. All rights reserved.

namespace Asp.Versioning.ApiExplorer;

using Asp.Versioning.Conventions;
using Asp.Versioning.OData;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Routing;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.AspNetCore.OData.Routing.Template;
using Microsoft.Extensions.Options;
using Microsoft.OData.Edm;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using static System.StringComparison;
using static ODataMetadataOptions;
using Opts = Microsoft.Extensions.Options.Options;

/// <summary>
/// Represents an API explorer that provides <see cref="ApiDescription">API descriptions</see> for actions represented by
/// <see cref="ControllerActionDescriptor">controller action descriptors</see> that are defined by
/// <see cref="ODataController">OData controllers</see> and are <see cref="ApiVersion">API version</see> aware.
/// </summary>
[CLSCompliant( false )]
public class ODataApiDescriptionProvider : IApiDescriptionProvider
{
    private const string NavigationProperty = "navigationProperty";
    private const string RelatedKey = "relatedKey";
    private static readonly int AfterApiVersioning = ApiVersioningOrder() - 100;
    private readonly IOptionsFactory<ODataOptions> odataOptionsFactory;
    private readonly IOptions<ODataApiExplorerOptions> options;
    private ODataOptions? odataOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ODataApiDescriptionProvider"/> class.
    /// </summary>
    /// <param name="modelMetadataProvider">The <see cref="IModelMetadataProvider">provider</see> used to retrieve model metadata.</param>
    /// <param name="modelTypeBuilder">The <see cref="IModelTypeBuilder">builder type builder</see> for explored models.</param>
    /// <param name="odataOptionsFactory">The <see cref="IOptionsFactory{TOptions}">factory</see> used to create
    /// <see cref="ODataOptions">OData options</see>.</param>
    /// <param name="options">The <see cref="IOptions{TOptions}">container</see> of configured
    /// <see cref="ODataApiExplorerOptions">API explorer options</see>.</param>
    public ODataApiDescriptionProvider(
        IModelMetadataProvider modelMetadataProvider,
        IModelTypeBuilder modelTypeBuilder,
        IOptionsFactory<ODataOptions> odataOptionsFactory,
        IOptions<ODataApiExplorerOptions> options )
    {
        ModelTypeBuilder = modelTypeBuilder ?? throw new ArgumentNullException( nameof( modelTypeBuilder ) );
        ModelMetadataProvider = modelMetadataProvider ?? throw new ArgumentNullException( nameof( modelMetadataProvider ) );
        this.odataOptionsFactory = odataOptionsFactory ?? throw new ArgumentNullException( nameof( odataOptionsFactory ) );
        this.options = options ?? throw new ArgumentNullException( nameof( options ) );
    }

    /// <summary>
    /// Gets or sets the order precedence of the current API description provider.
    /// </summary>
    /// <value>The order precedence of the current API description provider. The default value is -100.</value>
    public int Order { get; protected set; } = AfterApiVersioning;

    /// <summary>
    /// Gets the model type builder used by the API explorer.
    /// </summary>
    /// <value>The associated <see cref="IModelTypeBuilder">model type builder</see>.</value>
    protected IModelTypeBuilder ModelTypeBuilder { get; }

    /// <summary>
    /// Gets the model metadata provider associated with the description provider.
    /// </summary>
    /// <value>The <see cref="IModelMetadataProvider">provider</see> used to retrieve model metadata.</value>
    protected IModelMetadataProvider ModelMetadataProvider { get; }

    /// <summary>
    /// Gets the options associated with the API explorer.
    /// </summary>
    /// <value>The current <see cref="ApiExplorerOptions">API explorer options</see>.</value>
    protected ODataApiExplorerOptions Options => options.Value;

    /// <summary>
    /// Gets the associated OData options.
    /// </summary>
    /// <value>The configured <see cref="ODataOptions">OData options</see>.</value>
    protected ODataOptions ODataOptions => odataOptions ??= odataOptionsFactory.Create( Opts.DefaultName );

    /// <summary>
    /// Occurs after the providers have been executed.
    /// </summary>
    /// <param name="context">The current <see cref="ApiDescriptionProviderContext">execution context</see>.</param>
    /// <remarks>The default implementation performs no action.</remarks>
    public virtual void OnProvidersExecuted( ApiDescriptionProviderContext context )
    {
        ArgumentNullException.ThrowIfNull( context );

        var results = context.Results;
        var visited = new HashSet<ApiDescription>( capacity: results.Count, new ApiDescriptionComparer() );

        for ( var i = results.Count - 1; i >= 0; i-- )
        {
            var result = results[i];

            if ( result.ActionDescriptor.EndpointMetadata is not IList<object> endpointMetadata )
            {
                continue;
            }

            var metadata = endpointMetadata.OfType<IODataRoutingMetadata>().ToArray();
            var notOData = metadata.Length == 0;

            if ( notOData )
            {
                RemoveODataOptions( result );
                continue;
            }

            if ( !TryMatchModelVersion( result, metadata, out var matched ) ||
                 !visited.Add( result ) )
            {
                results.RemoveAt( i );
            }
            else if ( IsServiceDocument( matched.Template ) )
            {
                if ( !Options.MetadataOptions.HasFlag( ServiceDocument ) )
                {
                    results.RemoveAt( i );
                }
            }
            else if ( IsMetadata( matched.Template ) )
            {
                if ( !Options.MetadataOptions.HasFlag( Metadata ) )
                {
                    results.RemoveAt( i );
                }
            }
            else if ( IsNavigationPropertyLink( matched.Template ) )
            {
                results.RemoveAt( i );
                ExpandNavigationPropertyLinks( results, result, matched );
            }
            else
            {
                UpdateModelTypes( result, matched );
                UpdateFunctionCollectionParameters( result, matched );
            }
        }

        if ( results.Count > 0 )
        {
            ExploreQueryOptions( results );
        }
    }

    /// <summary>
    /// Occurs when the providers are being executed.
    /// </summary>
    /// <param name="context">The current <see cref="ApiDescriptionProviderContext">execution context</see>.</param>
    /// <remarks>The default implementation performs no operation.</remarks>
    public virtual void OnProvidersExecuting( ApiDescriptionProviderContext context ) { }

    /// <summary>
    /// Explores the OData query options for the specified API descriptions.
    /// </summary>
    /// <param name="apiDescriptions">The <see cref="IEnumerable{T}">sequence</see> of <see cref="ApiDescription">API descriptions</see> to explore.</param>
    protected virtual void ExploreQueryOptions( IEnumerable<ApiDescription> apiDescriptions )
    {
        var localODataOptions = ODataOptions;
        var localQueryOptions = Options.QueryOptions;
#pragma warning disable CS0618 // Type or member is obsolete
        var settings = new ODataQueryOptionSettings()
        {
            NoDollarPrefix = localODataOptions.EnableNoDollarQueryOptions,
            DescriptionProvider = localQueryOptions.DescriptionProvider,
            DefaultQuerySettings = localODataOptions.QuerySettings,
            ModelMetadataProvider = ModelMetadataProvider,
        };
#pragma warning restore CS0618 // Type or member is obsolete

        localQueryOptions.ApplyTo( apiDescriptions, settings );
    }

    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    private static int ApiVersioningOrder()
    {
        var policyManager = new SunsetPolicyManager( Opts.Create( new ApiVersioningOptions() ) );
        var options = Opts.Create( new ApiExplorerOptions() );
        var provider = new EmptyModelMetadataProvider();
        return new VersionedApiDescriptionProvider( policyManager, provider, options ).Order;
    }

    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    private static bool IsServiceDocument( ODataPathTemplate template ) => template.Count == 0;

    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    private static bool IsMetadata( ODataPathTemplate template ) => template.Count == 1 && template[0] is MetadataSegmentTemplate;

    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    private static bool IsNavigationPropertyLink( ODataPathTemplate template ) =>
        template.Count > 0 && template[^1] is NavigationLinkTemplateSegmentTemplate;

    private static bool TryMatchModelVersion(
        ApiDescription description,
        IODataRoutingMetadata[] items,
        [NotNullWhen( true )] out IODataRoutingMetadata? metadata )
    {
        if ( description.GetApiVersion() is not ApiVersion apiVersion )
        {
            // this should only happen if an odata endpoint is registered outside of api versioning:
            //
            // builder.Services.AddControllers().AddOData(options => options.AddRouteComponents(new EdmModel()));
            //
            // instead of:
            //
            // builder.Services.AddControllers().AddOData();
            // builder.Services.AddApiVersioning().AddOData(options => options.AddRouteComponents());
            metadata = default;
            return false;
        }

        for ( var i = 0; i < items.Length; i++ )
        {
            var item = items[i];
            var otherApiVersion = item.Model.GetApiVersion();

            if ( apiVersion.Equals( otherApiVersion ) )
            {
                metadata = item;
                return true;
            }
        }

        metadata = default;
        return false;
    }

    private static int FindNavigationPropertySegment( string[] segments )
    {
        for ( var i = 0; i < segments.Length; i++ )
        {
            var segment = segments[i].AsSpan();

            if ( segment.Length < 3 )
            {
                continue;
            }

            if ( segment[0] == '{' && segment[^1] == '}' )
            {
                segment = segment[1..^1];
            }

            if ( segment.Equals( NavigationProperty, OrdinalIgnoreCase ) )
            {
                return i;
            }
        }

        return -1;
    }

    private static void RemoveProperties( ApiDescription description, params string[] names )
    {
        var parameters = description.ParameterDescriptions;

        for ( var i = parameters.Count - 1; i >= 0; i-- )
        {
            var name = parameters[i].Name;

            for ( var j = 0; j < names.Length; j++ )
            {
                if ( name.Equals( names[j], OrdinalIgnoreCase ) )
                {
                    parameters.RemoveAt( i );
                    break;
                }
            }
        }
    }

    private static void RemoveODataOptions( ApiDescription description )
    {
        var parameters = description.ParameterDescriptions;

        for ( var i = 0; i < parameters.Count; i++ )
        {
            if ( parameters[i].Type.IsODataQueryOptions() )
            {
                parameters.RemoveAt( i );
                break;
            }
        }
    }

    private void ExpandNavigationPropertyLinks(
        ICollection<ApiDescription> descriptions,
        ApiDescription description,
        IODataRoutingMetadata metadata )
    {
        if ( string.IsNullOrEmpty( description.RelativePath ) )
        {
            return;
        }

        var template = metadata.Template;
        var entity = default( IEdmEntityType );

        // skip the last segment because we already know it's $ref
        for ( var i = 0; entity == null && i < template.Count - 1; i++ )
        {
            switch ( template[i] )
            {
                case EntitySetSegmentTemplate segment:
#pragma warning disable CS0618 // Type or member is obsolete
                    entity = segment.EntitySet.EntityType();
#pragma warning restore CS0618 // Type or member is obsolete
                    break;
                case SingletonSegmentTemplate segment:
#pragma warning disable CS0618 // Type or member is obsolete
                    entity = segment.Singleton.EntityType();
#pragma warning restore CS0618 // Type or member is obsolete
                    break;
            }
        }

        if ( entity == null )
        {
            return;
        }

        var create = !HttpMethod.Delete.Method.Equals( description.HttpMethod, OrdinalIgnoreCase );
        var segments = description.RelativePath!.Split( '/' ).ToArray();
        var index = FindNavigationPropertySegment( segments );

        foreach ( var property in entity.NavigationProperties() )
        {
            var expanded = description.Clone();

            RemoveProperties( expanded, NavigationProperty );

            if ( index >= 0 )
            {
                segments[index] = property.Name;
                expanded.RelativePath = string.Join( '/', segments );
            }

            if ( create )
            {
                AddOrReplaceIdBodyParameter( expanded );
            }

            descriptions.Add( expanded );
        }
    }

    private void AddOrReplaceIdBodyParameter( ApiDescription description )
    {
        var parameters = description.ParameterDescriptions;
        var parameter = default( ApiParameterDescription );
        var type = typeof( Uri );

        for ( var i = parameters.Count - 1; i >= 0; i-- )
        {
            parameter = parameters[i];

            if ( parameter.Source == BindingSource.Body &&
                 parameter.ParameterDescriptor?.ParameterType == type )
            {
                break;
            }

            parameter = default;
        }

        type = typeof( ODataId );
        var metadata = new ODataQueryOptionModelMetadata(
            ModelMetadataProvider,
            type,
            Options.RelatedEntityIdParameterDescription );

        if ( parameter == null )
        {
            parameter = new()
            {
                DefaultValue = default( ODataId ),
                Name = RelatedKey,
                ParameterDescriptor = new() { Name = RelatedKey },
            };

            parameters.Add( parameter );
        }

        parameter.IsRequired = true;
        parameter.ModelMetadata = metadata;
        parameter.ParameterDescriptor.ParameterType = type;
        parameter.Source = BindingSource.Body;
        parameter.Type = type;
    }

    private void UpdateModelTypes( ApiDescription description, IODataRoutingMetadata metadata )
    {
        var parameters = description.ParameterDescriptions;
        var responseTypes = description.SupportedResponseTypes;

        if ( parameters.Count == 0 && responseTypes.Count == 0 )
        {
            return;
        }

        var context = new TypeSubstitutionContext( metadata.Model, ModelTypeBuilder );

        for ( var i = parameters.Count - 1; i >= 0; i-- )
        {
            var parameter = parameters[i];

            if ( parameter.Type is not Type type )
            {
                continue;
            }

            if ( type.IsODataQueryOptions() || type.IsODataPath() )
            {
                // don't explore ODataQueryOptions or ODataPath
                parameters.RemoveAt( i );
                continue;
            }

            if ( type.IsODataActionParameters() )
            {
                var action = metadata.Template[^1] switch
                {
                    ActionSegmentTemplate segment => segment.Action,
                    ActionImportSegmentTemplate segment => segment.ActionImport.Action,
                    _ => default,
                };
                var apiVersion = description.GetApiVersion()!;
                var controllerName = ( (ControllerActionDescriptor) description.ActionDescriptor ).ControllerName;

                type = ModelTypeBuilder.NewActionParameters( metadata.Model, action!, controllerName, apiVersion );
            }
            else
            {
                type = type.SubstituteIfNecessary( context );
            }

            parameter.Type = type;
            parameter.ModelMetadata = parameter.ModelMetadata.SubstituteIfNecessary( type );
        }

        for ( var i = 0; i < responseTypes.Count; i++ )
        {
            var responseType = responseTypes[i];

            if ( responseType.Type is not Type type ||
                 responseType.ModelMetadata is not ModelMetadata modelMetadata )
            {
                continue;
            }

            type = type.SubstituteIfNecessary( context );
            responseType.Type = type;
            responseType.ModelMetadata = modelMetadata.SubstituteIfNecessary( type );
        }
    }

    private static void UpdateFunctionCollectionParameters( ApiDescription description, IODataRoutingMetadata metadata )
    {
        var parameters = description.ParameterDescriptions;

        if ( parameters.Count == 0 )
        {
            return;
        }

        var function = default( IEdmFunction );
        var mapping = default( IDictionary<string, string> );

        for ( var i = 0; i < metadata.Template.Count; i++ )
        {
            var segment = metadata.Template[i];

            if ( segment is FunctionSegmentTemplate func )
            {
                function = func.Function;
                mapping = func.ParameterMappings;
                break;
            }
            else if ( segment is FunctionImportSegmentTemplate import )
            {
                function = import.FunctionImport.Function;
                mapping = import.ParameterMappings;
                break;
            }
        }

        if ( function is null || mapping is null )
        {
            return;
        }

        var name = default( string );

        foreach ( var parameter in function.Parameters )
        {
            if ( parameter.Type.IsCollection() &&
                 mapping.TryGetValue( parameter.Name, out name ) &&
                 parameters.SingleOrDefault( p => p.Name == name ) is { } param )
            {
                param.Source = BindingSource.Path;
                break;
            }
        }

        var path = description.RelativePath;

        if ( string.IsNullOrEmpty( name ) || string.IsNullOrEmpty( path ) )
        {
            return;
        }

        var span = name.AsSpan();
        Span<char> oldValue = stackalloc char[name.Length + 2];
        Span<char> newValue = stackalloc char[name.Length + 4];

        newValue[1] = oldValue[0] = '{';
        newValue[^2] = oldValue[^1] = '}';
        newValue[0] = '[';
        newValue[^1] = ']';
        span.CopyTo( oldValue.Slice( 1, name.Length ) );
        span.CopyTo( newValue.Slice( 2, name.Length ) );

        description.RelativePath = path.Replace( oldValue.ToString(), newValue.ToString(), Ordinal );
    }

    private sealed class ApiDescriptionComparer : IEqualityComparer<ApiDescription>
    {
        private readonly IEqualityComparer<string?> comparer = StringComparer.OrdinalIgnoreCase;

        public bool Equals( ApiDescription? x, ApiDescription? y )
        {
            if ( x is null )
            {
                return y is null;
            }

            if ( y is null )
            {
                return false;
            }

            return GetHashCode( x ) == GetHashCode( y );
        }

        public int GetHashCode( [DisallowNull] ApiDescription obj )
        {
            var hash = default( HashCode );

            hash.Add( obj.GroupName );
            hash.Add( obj.RelativePath, comparer );
            hash.Add( ( (ControllerActionDescriptor) obj.ActionDescriptor ).MethodInfo.GetHashCode() );

            return hash.ToHashCode();
        }
    }
}