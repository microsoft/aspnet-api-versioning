namespace Microsoft.Web.Http.Dispatcher
{
    using Microsoft.Web.Http.Controllers;
    using Microsoft.Web.Http.Routing;
    using Microsoft.Web.Http.Versioning;
    using Microsoft.Web.Http.Versioning.Conventions;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Web.Http;
    using System.Web.Http.Controllers;
    using System.Web.Http.Dispatcher;
    using System.Threading;
    using static Microsoft.Web.Http.Versioning.ErrorCodes;
    using static System.StringComparer;

    /// <summary>
    /// Represents the logic for selecting a versioned controller.
    /// </summary>
    public class ApiVersionControllerSelector : IHttpControllerSelector
    {
        readonly HttpConfiguration configuration;
        readonly ApiVersioningOptions options;
        readonly HttpControllerTypeCache controllerTypeCache;
        readonly Lazy<ConcurrentDictionary<string, HttpControllerDescriptorGroup>> controllerInfoCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApiVersionControllerSelector"/> class.
        /// </summary>
        /// <param name="configuration">The <see cref="HttpConfiguration">configuration</see> to initialize
        /// the controller selector with.</param>
        /// <param name="options">The <see cref="ApiVersioningOptions">service versioning options</see>
        /// associated with the controller selector.</param>
        public ApiVersionControllerSelector( HttpConfiguration configuration, ApiVersioningOptions options )
        {
            this.configuration = configuration;
            this.options = options;
            controllerInfoCache = new Lazy<ConcurrentDictionary<string, HttpControllerDescriptorGroup>>( InitializeControllerInfoCache, LazyThreadSafetyMode.PublicationOnly );
            controllerTypeCache = new HttpControllerTypeCache( this.configuration );
        }

        /// <summary>
        /// Creates and returns a controller descriptor mapping.
        /// </summary>
        /// <returns>A <see cref="IDictionary{TKey,TValue}">collection</see> of route-to-controller mapping.</returns>
        public virtual IDictionary<string, HttpControllerDescriptor> GetControllerMapping()
        {
            var mapping = controllerInfoCache.Value.Where( p => p.Value.Count > 0 );
            return mapping.ToDictionary( p => p.Key, p => (HttpControllerDescriptor) p.Value, OrdinalIgnoreCase );
        }

        /// <summary>
        /// Selects and returns the controller descriptor to invoke given the provided request.
        /// </summary>
        /// <param name="request">The <see cref="HttpRequestMessage">request</see> to get a controller descriptor for.</param>
        /// <returns>The <see cref="HttpControllerDescriptor">controller descriptor</see> that matches the specified <paramref name="request"/>.</returns>
        public virtual HttpControllerDescriptor? SelectController( HttpRequestMessage request )
        {
            EnsureRequestHasValidApiVersion( request );

            var context = new ControllerSelectionContext( request, GetControllerName, controllerInfoCache );

            if ( context.RequestedVersion == null )
            {
                if ( options.AssumeDefaultVersionWhenUnspecified )
                {
                    context.RequestedVersion = options.ApiVersionSelector.SelectVersion( context.Request, context.AllVersions );
                }
            }

            var conventionRouteSelector = new ConventionRouteControllerSelector( controllerTypeCache );
            var conventionRouteResult = default( ControllerSelectionResult );
            var exceptionFactory = new HttpResponseExceptionFactory( request, new Lazy<ApiVersionModel>( () => context.AllVersions ) );

            if ( context.RouteData == null )
            {
                conventionRouteResult = conventionRouteSelector.SelectController( context );

                if ( conventionRouteResult.Succeeded )
                {
                    EnsureUrlHelper( request );
                    return request.ApiVersionProperties().SelectedController = conventionRouteResult.Controller;
                }

                throw exceptionFactory.NewNotFoundOrBadRequestException( conventionRouteResult, default );
            }

            var directRouteSelector = new DirectRouteControllerSelector();
            var directRouteResult = directRouteSelector.SelectController( context );

            if ( directRouteResult.Succeeded )
            {
                EnsureUrlHelper( request );
                return request.ApiVersionProperties().SelectedController = directRouteResult.Controller;
            }

            conventionRouteResult = conventionRouteSelector.SelectController( context );

            if ( conventionRouteResult.Succeeded )
            {
                EnsureUrlHelper( request );
                return request.ApiVersionProperties().SelectedController = conventionRouteResult.Controller;
            }

            throw exceptionFactory.NewNotFoundOrBadRequestException( conventionRouteResult, directRouteResult );
        }

        /// <summary>
        /// Gets the name of the controller for the specified request.
        /// </summary>
        /// <param name="request">The <see cref="HttpRequestMessage">request</see> to the controller name for.</param>
        /// <returns>The name of the controller for the specified <paramref name="request"/>.</returns>
        public virtual string? GetControllerName( HttpRequestMessage request )
        {
            var routeData = request.GetRouteData();

            if ( routeData == null )
            {
                return null;
            }

            if ( routeData.Values.TryGetValue( RouteDataTokenKeys.Controller, out string controller ) )
            {
                return controller;
            }

            var configuration = request.GetConfiguration();
            var routes = configuration.Routes;
            var context = request.GetRequestContext();
            var virtualPathRoot = routes.VirtualPathRoot;

            if ( context != null )
            {
                virtualPathRoot = context.VirtualPathRoot ?? string.Empty;
            }

            // HACK: do NOT use a normal 'for' loop here because the IIS implementation does not support indexing
            foreach ( var route in routes )
            {
                var otherRouteData = route.GetRouteData( virtualPathRoot, request );

                if ( otherRouteData != null &&
                    !routeData.Equals( otherRouteData ) &&
                     otherRouteData.Values.TryGetValue( RouteDataTokenKeys.Controller, out controller ) )
                {
                    break;
                }
            }

            return controller;
        }

        ConcurrentDictionary<string, HttpControllerDescriptorGroup> InitializeControllerInfoCache()
        {
            var options = configuration.GetApiVersioningOptions();
            var implicitVersionModel = new ApiVersionModel( options.DefaultApiVersion );
            var conventions = options.Conventions;
            var actionSelector = configuration.Services.GetActionSelector();
            var mapping = new ConcurrentDictionary<string, HttpControllerDescriptorGroup>( OrdinalIgnoreCase );

            foreach ( var pair in controllerTypeCache.Cache )
            {
                var key = pair.Key;
                var descriptors = new List<HttpControllerDescriptor>();

                foreach ( var grouping in pair.Value )
                {
                    foreach ( var type in grouping )
                    {
                        var descriptor = new HttpControllerDescriptor( configuration, key, type );

                        if ( !conventions.ApplyTo( descriptor ) )
                        {
                            ApplyAttributeOrImplicitConventions( descriptor, actionSelector, implicitVersionModel );
                        }

                        descriptors.Add( descriptor );
                    }
                }

                var innerDescriptors = ApplyCollatedModels( descriptors, actionSelector );
                var descriptorGroup = new HttpControllerDescriptorGroup( configuration, key, innerDescriptors );

                mapping.TryAdd( key, descriptorGroup );
            }

            return mapping;
        }

        static bool IsDecoratedWithAttributes( HttpControllerDescriptor controller )
        {
            return controller.GetCustomAttributes<IApiVersionProvider>().Count > 0 ||
                   controller.GetCustomAttributes<IApiVersionNeutral>().Count > 0;
        }

        static void ApplyImplicitConventions( HttpControllerDescriptor controller, IHttpActionSelector actionSelector, ApiVersionModel implicitVersionModel )
        {
            controller.SetApiVersionModel( implicitVersionModel );

            var actions = actionSelector.GetActionMapping( controller ).SelectMany( g => g );

            foreach ( var action in actions )
            {
                action.SetProperty( implicitVersionModel );
            }
        }

        static void ApplyAttributeOrImplicitConventions( HttpControllerDescriptor controller, IHttpActionSelector actionSelector, ApiVersionModel implicitVersionModel )
        {
            if ( IsDecoratedWithAttributes( controller ) )
            {
                var conventions = new ControllerApiVersionConventionBuilder( controller.ControllerType );
                conventions.ApplyTo( controller );
            }
            else
            {
                ApplyImplicitConventions( controller, actionSelector, implicitVersionModel );
            }
        }

        static HttpControllerDescriptor[] ApplyCollatedModels( List<HttpControllerDescriptor> controllers, IHttpActionSelector actionSelector )
        {
            var supported = new HashSet<ApiVersion>();
            var deprecated = new HashSet<ApiVersion>();
            var controllerModels = new List<ApiVersionModel>( controllers.Count );
            var actionModels = new List<ApiVersionModel>( controllers.Count );
            var visitedControllers = new List<Tuple<HttpControllerDescriptor, ApiVersionModel>>( controllers.Count );
            var visitedActions = new List<Tuple<HttpActionDescriptor, ApiVersionModel>>( controllers.Count );

            for ( var i = 0; i < controllers.Count; i++ )
            {
                // 1 - collate controller versions
                var controller = controllers[i];
                var model = controller.GetApiVersionModel();

                if ( model.IsApiVersionNeutral )
                {
                    continue;
                }

                controllerModels.Add( model );
                visitedControllers.Add( Tuple.Create( controller, model ) );

                // 2 - collate action versions
                var actions = actionSelector.GetActionMapping( controller ).SelectMany( g => g );

                foreach ( var action in actions )
                {
                    model = action.GetApiVersionModel();

                    if ( model.IsApiVersionNeutral )
                    {
                        continue;
                    }

                    actionModels.Add( model );
                    visitedActions.Add( Tuple.Create( action, model ) );
                }
            }

            // 3 - apply collated action model
            var collatedModel = actionModels.Aggregate();

            for ( var i = 0; i < visitedActions.Count; i++ )
            {
                var (action, model) = visitedActions[i];

                action.SetProperty( model.Aggregate( collatedModel ) );
            }

            // 4 - apply collated controller model
            // note: allows controllers to report versions in 400s even when an action is unmatched
            controllerModels.Add( collatedModel );
            collatedModel = controllerModels.Aggregate();

            for ( var i = 0; i < visitedControllers.Count; i++ )
            {
                var (controller, model) = visitedControllers[i];

                controller.SetApiVersionModel( model.Aggregate( collatedModel ) );
            }

            return controllers.ToArray();
        }

        static void EnsureRequestHasValidApiVersion( HttpRequestMessage request )
        {
            try
            {
                var apiVersion = request.GetRequestedApiVersion();
            }
            catch ( AmbiguousApiVersionException ex )
            {
                var response = request.GetApiVersioningOptions().ErrorResponses;
                throw new HttpResponseException( response.BadRequest( request, AmbiguousApiVersion, ex.Message ) );
            }
        }

        static void EnsureUrlHelper( HttpRequestMessage request )
        {
            var context = request.GetRequestContext();

            if ( context == null || context.Url is ApiVersionUrlHelper )
            {
                return;
            }

            var options = request.GetApiVersioningOptions();

            if ( options.ApiVersionReader.VersionsByUrlSegment() )
            {
                context.Url = new ApiVersionUrlHelper( context.Url );
            }
        }
    }
}
