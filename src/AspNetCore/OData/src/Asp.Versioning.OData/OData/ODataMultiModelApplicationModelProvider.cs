﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.

#pragma warning disable CA1812

namespace Asp.Versioning.OData;

using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.OData;
using Microsoft.AspNetCore.OData.Routing.Conventions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OData.Edm;
using System.Runtime.CompilerServices;
using static System.Linq.Expressions.Expression;

internal sealed class ODataMultiModelApplicationModelProvider : IApplicationModelProvider
{
    internal static readonly Type ODataRoutingApplicationModelProviderType = GetDefaultApplicationModelProviderType();
    private static readonly Func<IOptions<ODataOptions>, IApplicationModelProvider> NewODataApplicationModelProvider = CreateActivator( ODataRoutingApplicationModelProviderType );
    private readonly IODataApiVersionCollectionProvider apiVersionCollectionProvider;
    private readonly VersionedODataOptions versionedODataOptions;
    private readonly IOptionsFactory<ODataOptions> optionsFactory;
    private readonly IOptions<ODataApiVersioningOptions> optionsHolder;

    public ODataMultiModelApplicationModelProvider(
        IODataApiVersionCollectionProvider apiVersionCollectionProvider,
        VersionedODataOptions versionedODataOptions,
        IOptionsFactory<ODataOptions> optionsFactory,
        IOptions<ODataApiVersioningOptions> optionsHolder )
    {
        this.apiVersionCollectionProvider = apiVersionCollectionProvider;
        this.versionedODataOptions = versionedODataOptions;
        this.optionsFactory = optionsFactory;
        this.optionsHolder = optionsHolder;
    }

    internal static int DefaultODataOrder { get; } = NewODataApplicationModelProvider( Options.Create( new ODataOptions() ) ).Order;

    public int Order { get; } = DefaultODataOrder;

    public void OnProvidersExecuting( ApplicationModelProviderContext context )
    {
        // the decorated implementation doesn't do anything here so defer initialization as long as possible
        //
        // REF: https://github.com/OData/AspNetCoreOData/blob/main/src/Microsoft.AspNetCore.OData/Routing/ODataRoutingApplicationModelProvider.cs#L119
    }

    public void OnProvidersExecuted( ApplicationModelProviderContext context )
    {
        var versioningOptions = optionsHolder.Value;
        var builder = versioningOptions.ModelBuilder;
        Dictionary<ApiVersion, ODataOptions> mapping;

        if ( versioningOptions.HasConfigurations )
        {
            var capacity = versioningOptions.Configurations.Count * apiVersionCollectionProvider.ApiVersions.Count;
            mapping = new( capacity );

            foreach ( var (prefix, configureAction) in versioningOptions.Configurations )
            {
                var models = builder.GetEdmModels( prefix );
                AddRouteComponents( models, mapping, prefix, configureAction );
            }
        }
        else
        {
            var models = builder.GetEdmModels();

            if ( models.Count == 0 )
            {
                return;
            }

            // if at least one model is built, then we can still register things without
            // requiring explicit calls to AddRouteComponents because one or more models
            // were constructed from DI via IModelConfiguration
            var capacity = apiVersionCollectionProvider.ApiVersions.Count;
            static void NoConfig( IServiceCollection sc )
            {
            }

            mapping = new( capacity );
            AddRouteComponents( models, mapping, string.Empty, NoConfig );
        }

        foreach ( var options in mapping.Values )
        {
            var index = FindAttributeRouteConvention( options );
            IApplicationModelProvider provider;

            if ( index > -1 )
            {
                var conventions = options.Conventions;
                var convention = conventions[index];

                // HACK: the default constructor doesn't consider inheritance of AttributeRoutingConvention
                //       which results in the wrong initialization logic. temporarily remove the convention,
                //       initialize the provider, then re-add the convention in the same location
                //
                // REF: https://github.com/OData/AspNetCoreOData/blob/main/src/Microsoft.AspNetCore.OData/Routing/ODataRoutingApplicationModelProvider.cs#L33
                conventions.RemoveAt( index );
                provider = NewODataApplicationModelProvider( Options.Create( options ) );
                conventions.Insert( index, convention );
            }
            else
            {
                provider = NewODataApplicationModelProvider( Options.Create( options ) );
            }

            provider.OnProvidersExecuted( context );
        }

        // HACK: there are intrinsically a couple of issues here:
        //
        // 1. ASP.NET Core creates an ActionDescriptor per SelectorModel in an ActionModel
        // 2. OData adds a SelectorModel per EDM
        // 3. ApiVersionMetadata has already be computed and added to EndpointMetadata
        //
        // this becomes a problem when there are multiple EDMs and a single action implementation
        // maps to more than one EDM or a dynamically added OData endpoint is added without ApiVersionMetadata.
        //
        // REF: https://github.com/dotnet/aspnetcore/blob/main/src/Mvc/Mvc.Core/src/ApplicationModels/ActionAttributeRouteModel.cs
        // REF: https://github.com/OData/AspNetCoreOData/blob/main/src/Microsoft.AspNetCore.OData/Extensions/ActionModelExtensions.cs#L148
        CopyApiVersionEndpointMetadata( context.Result.Controllers );

        versionedODataOptions.Mapping = mapping;
    }

    [MethodImpl( MethodImplOptions.AggressiveInlining )]
    private static Type GetDefaultApplicationModelProviderType()
    {
        const string TypeName = "Microsoft.AspNetCore.OData.Routing.ODataRoutingApplicationModelProvider";
        var assemblyName = typeof( ODataOptions ).Assembly.GetName().Name;
        return Type.GetType( $"{TypeName}, {assemblyName}", throwOnError: true, ignoreCase: false )!;
    }

    private static Func<IOptions<ODataOptions>, IApplicationModelProvider> CreateActivator( Type type )
    {
        var options = Parameter( typeof( IOptions<ODataOptions> ), "options" );
        var @new = New( type.GetConstructors()[0], options );
        var lambda = Lambda<Func<IOptions<ODataOptions>, IApplicationModelProvider>>( @new, options );

        return lambda.Compile();
    }

    private void AddRouteComponents(
            IReadOnlyList<IEdmModel> models,
            Dictionary<ApiVersion, ODataOptions> mappings,
            string prefix,
            Action<IServiceCollection> configureAction )
    {
        for ( var i = 0; i < models.Count; i++ )
        {
            var model = models[i];
            var version = model.GetAnnotationValue<ApiVersionAnnotation>( model ).ApiVersion;

            if ( !mappings.TryGetValue( version, out var options ) )
            {
                options = optionsFactory.Create( Options.DefaultName );
                mappings.Add( version, options );
            }

            options.AddRouteComponents( prefix, model, configureAction );
        }
    }

    private static int FindAttributeRouteConvention( ODataOptions options )
    {
        var conventions = options.Conventions;

        for ( var i = 0; i < conventions.Count; i++ )
        {
            if ( conventions[i] is AttributeRoutingConvention )
            {
                return i;
            }
        }

        return -1;
    }

    private static void CopyApiVersionEndpointMetadata( IList<ControllerModel> controllers )
    {
        for ( var i = 0; i < controllers.Count; i++ )
        {
            var actions = controllers[i].Actions;

            for ( var j = 0; j < actions.Count; j++ )
            {
                var selectors = actions[j].Selectors;

                if ( selectors.Count < 2 )
                {
                    continue;
                }

                ApiVersionMetadata? metadata = null;
                for ( var m = 0; m < selectors.Count; m++ )
                {
                    metadata = selectors[m].EndpointMetadata.OfType<ApiVersionMetadata>().FirstOrDefault();
                    if ( metadata != null )
                    {
                        if ( m != 0 )
                        {
                            var tmpSelector = selectors[m];
                            selectors[m] = selectors[0];
                            selectors[0] = tmpSelector;
                        }

                        break;
                    }
                }

                if ( metadata is null )
                {
                    continue;
                }

                for ( var k = 1; k < selectors.Count; k++ )
                {
                    var endpointMetadata = selectors[k].EndpointMetadata;
                    var found = false;

                    for ( var l = 0; l < endpointMetadata.Count; l++ )
                    {
                        if ( endpointMetadata[l] is not ApiVersionMetadata )
                        {
                            continue;
                        }

                        endpointMetadata[l] = metadata;
                        found = true;
                        break;
                    }

                    if ( !found )
                    {
                        endpointMetadata.Add( metadata );
                    }
                }
            }
        }
    }
}