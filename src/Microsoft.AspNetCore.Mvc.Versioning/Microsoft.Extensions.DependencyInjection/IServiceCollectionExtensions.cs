﻿namespace Microsoft.Extensions.DependencyInjection
{
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.Abstractions;
    using Microsoft.AspNetCore.Mvc.ApplicationModels;
    using Microsoft.AspNetCore.Mvc.Infrastructure;
    using Microsoft.AspNetCore.Mvc.Routing;
    using Microsoft.AspNetCore.Mvc.Versioning;
    using Microsoft.AspNetCore.Routing;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using Microsoft.Extensions.Options;
    using System;
    using System.Linq;
    using static ServiceDescriptor;

    /// <summary>
    /// Provides extension methods for the <see cref="IServiceCollection"/> interface.
    /// </summary>
    [CLSCompliant( false )]
    public static class IServiceCollectionExtensions
    {
        /// <summary>
        /// Adds service API versioning to the specified services collection.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection">services</see> available in the application.</param>
        /// <returns>The original <paramref name="services"/> object.</returns>
        public static IServiceCollection AddApiVersioning( this IServiceCollection services )
        {
            AddApiVersioningServices( services );
            return services;
        }

        /// <summary>
        /// Adds service API versioning to the specified services collection.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection">services</see> available in the application.</param>
        /// <param name="setupAction">An <see cref="Action{T}">action</see> used to configure the provided options.</param>
        /// <returns>The original <paramref name="services"/> object.</returns>
        public static IServiceCollection AddApiVersioning( this IServiceCollection services, Action<ApiVersioningOptions> setupAction )
        {
            AddApiVersioningServices( services );
            services.Configure( setupAction );
            return services;
        }

        static void AddApiVersioningServices( IServiceCollection services )
        {
            if ( services == null )
            {
                throw new ArgumentNullException( nameof( services ) );
            }

            services.Add( Singleton( sp => sp.GetRequiredService<IOptions<ApiVersioningOptions>>().Value.ApiVersionReader ) );
            services.Add( Singleton( sp => sp.GetRequiredService<IOptions<ApiVersioningOptions>>().Value.ApiVersionSelector ) );
            services.Add( Singleton( sp => sp.GetRequiredService<IOptions<ApiVersioningOptions>>().Value.ErrorResponses ) );
            services.Replace( Singleton<IActionSelector, ApiVersionActionSelector>() );
            services.TryAddSingleton<IApiVersionRoutePolicy, DefaultApiVersionRoutePolicy>();
            services.TryAddSingleton<IApiControllerFilter, DefaultApiControllerFilter>();
            services.TryAddSingleton<ReportApiVersionsAttribute>();
            services.AddSingleton<ApplyContentTypeVersionActionFilter>();
            services.TryAddSingleton( OnRequestIReportApiVersions );
            services.TryAddEnumerable( Transient<IPostConfigureOptions<MvcOptions>, ApiVersioningMvcOptionsSetup>() );
            services.TryAddEnumerable( Transient<IPostConfigureOptions<RouteOptions>, ApiVersioningRouteOptionsSetup>() );
            services.TryAddEnumerable( Transient<IApplicationModelProvider, ApiVersioningApplicationModelProvider>() );
            services.TryAddEnumerable( Transient<IActionDescriptorProvider, ApiVersionCollator>() );
            services.TryAddEnumerable( Transient<IApiControllerSpecification, ApiBehaviorSpecification>() );
            services.TryAddEnumerable( Singleton<MatcherPolicy, ApiVersionMatcherPolicy>() );
            services.AddTransient<IStartupFilter, AutoRegisterMiddleware>();
            services.Replace( WithUrlHelperFactoryDecorator( services ) );
        }

        static IReportApiVersions OnRequestIReportApiVersions( IServiceProvider serviceProvider )
        {
            var options = serviceProvider.GetRequiredService<IOptions<ApiVersioningOptions>>().Value;

            if ( options.ReportApiVersions )
            {
                return new DefaultApiVersionReporter();
            }

            return new DoNotReportApiVersions();
        }

        static object CreateInstance( this IServiceProvider services, ServiceDescriptor descriptor )
        {
            if ( descriptor.ImplementationInstance != null )
            {
                return descriptor.ImplementationInstance;
            }

            if ( descriptor.ImplementationFactory != null )
            {
                return descriptor.ImplementationFactory( services );
            }

            return ActivatorUtilities.GetServiceOrCreateInstance( services, descriptor.ImplementationType );
        }

        static ServiceDescriptor WithUrlHelperFactoryDecorator( IServiceCollection services )
        {
            var descriptor = services.FirstOrDefault( sd => sd.ServiceType == typeof( IUrlHelperFactory ) );
            var lifetime = ServiceLifetime.Singleton;
            Func<IServiceProvider, object> instantiate = sp => new UrlHelperFactory();

            if ( descriptor != null )
            {
                lifetime = descriptor.Lifetime;
                instantiate = sp => sp.CreateInstance( descriptor );
            }

            IUrlHelperFactory NewFactory( IServiceProvider serviceProvider )
            {
                var decorated = instantiate( serviceProvider );
                var options = serviceProvider.GetRequiredService<IOptions<ApiVersioningOptions>>().Value;
                var instance = decorated;

                if ( options.ApiVersionReader.VersionsByUrlSegment() )
                {
                    var factory = ActivatorUtilities.CreateFactory( typeof( ApiVersionUrlHelperFactory ), new[] { typeof( IUrlHelperFactory ) } );
                    instance = factory( serviceProvider, new[] { decorated } );
                }

                return (IUrlHelperFactory) instance;
            }

            return Describe( typeof( IUrlHelperFactory ), NewFactory, lifetime );
        }
    }
}