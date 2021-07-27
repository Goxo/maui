﻿using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Hosting.Internal;

namespace Microsoft.Maui
{
	public sealed class MauiAppBuilder
	{
		private MauiAppBuilder()
		{
		}

		public static MauiAppBuilder CreateBuilder() => new MauiAppBuilder(); // <-- perhaps set some defaults here?

		/// <summary>
		/// A collection of services for the application to compose. This is useful for adding user provided or framework provided services.
		/// </summary>
		public IServiceCollection Services { get; } = new ServiceCollection();

		readonly Dictionary<Type, List<Action<HostBuilderContext, IMauiServiceBuilder>>> _configureServiceBuilderActions = new();
		public MauiAppBuilder ConfigureMauiHandlers(Action<IMauiHandlersCollection> configureDelegate)
		{
			ConfigureServices<HandlerCollectionBuilder>((_, handlers) => configureDelegate(handlers));
			return this;
		}

		public MauiAppBuilder ConfigureMauiHandlers(Action<HostBuilderContext, IMauiHandlersCollection> configureDelegate)
		{
			ConfigureServices<HandlerCollectionBuilder>(configureDelegate);
			return this;
		}

		public MauiAppBuilder ConfigureFonts(Action<IFontCollection> configureDelegate)
		{
			Services.AddSingleton<IEmbeddedFontLoader>(svc => new EmbeddedFontLoader(svc.CreateLogger<EmbeddedFontLoader>()));
			Services.AddSingleton<IFontRegistrar>(svc => new FontRegistrar(svc.GetRequiredService<IEmbeddedFontLoader>(), svc.CreateLogger<FontRegistrar>()));
			Services.AddSingleton<IFontManager>(svc => new FontManager(svc.GetRequiredService<IFontRegistrar>(), svc.CreateLogger<FontManager>()));

			Services.AddSingleton<FontsRegistration>(new FontsRegistration(configureDelegate));
			Services.AddSingleton<IMauiInitializeService, FontInitializer>();
			return this;
		}


		internal class FontsRegistration
		{
			private readonly Action<FontCollection> _registerFonts;

			public FontsRegistration(Action<FontCollection> registerFonts)
			{
				_registerFonts = registerFonts;
			}

			internal void AddFonts(FontCollection fonts)
			{
				_registerFonts(fonts);
			}
		}

		internal class FontInitializer : IMauiInitializeService
		{
			private readonly IEnumerable<FontsRegistration> _fontsRegistrations;

			public FontInitializer(IEnumerable<FontsRegistration> fontsRegistrations)
			{
				_fontsRegistrations = fontsRegistrations;
			}

			public void Initialize(HostBuilderContext context, IServiceProvider services)
			{
				var fontRegistrar = services.GetService<IFontRegistrar>();
				if (fontRegistrar == null)
					return;

				if (_fontsRegistrations != null)
				{
					var fontsBuilder = new FontCollection();

					// Run all the user-defined registrations
					foreach (var font in _fontsRegistrations)
					{
						font.AddFonts(fontsBuilder);
					}

					// Register the fonts in the registrar
					foreach (var font in fontsBuilder)
					{
						if (font.Assembly == null)
							fontRegistrar.Register(font.Filename, font.Alias);
						else
							fontRegistrar.Register(font.Filename, font.Alias, font.Assembly);
					}
				}
			}
		}

		readonly List<Action<HostBuilderContext, IConfigurationBuilder>> _configureAppConfigActions = new List<Action<HostBuilderContext, IConfigurationBuilder>>();
		readonly List<Action<IConfigurationBuilder>> _configureHostConfigActions = new List<Action<IConfigurationBuilder>>();

		public MauiAppBuilder ConfigureAppConfiguration(Action<HostBuilderContext, IConfigurationBuilder> configureDelegate)
		{
			_configureAppConfigActions.Add(configureDelegate ?? throw new ArgumentNullException(nameof(configureDelegate)));
			return this;
		}

		public MauiAppBuilder ConfigureAppConfiguration(Action<IConfigurationBuilder> configureDelegate)
		{
			ConfigureAppConfiguration((_, config) => configureDelegate(config));
			return this;
		}

		public MauiAppBuilder ConfigureHostConfiguration(Action<IConfigurationBuilder> configureDelegate)
		{
			_configureHostConfigActions.Add(configureDelegate ?? throw new ArgumentNullException(nameof(configureDelegate)));
			return this;
		}

		public MauiAppBuilder ConfigureImageSources()
		{
			ConfigureImageSources(services =>
			{
				services.AddService<IFileImageSource>(svcs => new FileImageSourceService(svcs.GetService<IImageSourceServiceConfiguration>(), svcs.CreateLogger<FileImageSourceService>()));
				services.AddService<IFontImageSource>(svcs => new FontImageSourceService(svcs.GetRequiredService<IFontManager>(), svcs.CreateLogger<FontImageSourceService>()));
				services.AddService<IStreamImageSource>(svcs => new StreamImageSourceService(svcs.CreateLogger<StreamImageSourceService>()));
				services.AddService<IUriImageSource>(svcs => new UriImageSourceService(svcs.CreateLogger<UriImageSourceService>()));
			});
			return this;
		}

		public MauiAppBuilder ConfigureImageSources(Action<IImageSourceServiceCollection> configureDelegate)
		{
			ConfigureServices<ImageSourceServiceBuilder>((_, services) => configureDelegate(services));
			return this;
		}

		public MauiAppBuilder ConfigureImageSources(Action<HostBuilderContext, IImageSourceServiceCollection> configureDelegate)
		{
			ConfigureServices<ImageSourceServiceBuilder>(configureDelegate);
			return this;
		}

		class ImageSourceServiceBuilder : MauiServiceCollection, IImageSourceServiceCollection, IMauiServiceBuilder
		{
			public void ConfigureServices(HostBuilderContext context, IServiceCollection services)
			{
				services.AddSingleton<IImageSourceServiceConfiguration, ImageSourceServiceConfiguration>();
				services.AddSingleton<IImageSourceServiceProvider>(svcs => new ImageSourceServiceProvider(this, svcs));
			}
		}

		class HandlerCollectionBuilder : MauiHandlersCollection, IMauiServiceBuilder
		{
			public void ConfigureServices(HostBuilderContext context, IServiceCollection services)
			{
				var provider = new MauiHandlersServiceProvider(this);

				services.AddSingleton<IMauiHandlersServiceProvider>(provider);
			}
		}


		public MauiAppBuilder ConfigureServices<TBuilder>(Action<HostBuilderContext, TBuilder> configureDelegate)
			where TBuilder : IMauiServiceBuilder, new()
		{
			_ = configureDelegate ?? throw new ArgumentNullException(nameof(configureDelegate));

			var key = typeof(TBuilder);
			if (!_configureServiceBuilderActions.TryGetValue(key, out var list))
			{
				list = new List<Action<HostBuilderContext, IMauiServiceBuilder>>();
				_configureServiceBuilderActions.Add(key, list);
			}

			list.Add((context, builder) => configureDelegate(context, (TBuilder)builder));

			return this;
		}

		public IServiceProvider FinalizeInternals()
		{
			// AppConfig
			BuildHostConfiguration();
			BuildAppConfiguration();
			if (_appConfiguration != null)
				Services.AddSingleton(_appConfiguration);

			// ConfigureServices
			var properties = new Dictionary<object, object>();
			var builderContext = new HostBuilderContext(properties); // TODO: Should get this from somewhere...

			foreach (var pair in _configureServiceBuilderActions)
			{
				var instance = (IMauiServiceBuilder)Activator.CreateInstance(pair.Key)!;

				foreach (var action in pair.Value)
				{
					action(builderContext, instance);
				}

				instance.ConfigureServices(builderContext, Services);
			}

			var serviceProvider = Services.BuildServiceProvider();

			var initServices = serviceProvider.GetService<IEnumerable<IMauiInitializeService>>();
			if (initServices != null)
			{
				foreach (var instance in initServices)
				{
					instance.Initialize(builderContext, serviceProvider);
				}
			}

			return serviceProvider;
		}

		IConfiguration? _hostConfiguration;
		IConfiguration? _appConfiguration;

		void BuildHostConfiguration()
		{
			var configBuilder = new ConfigurationBuilder();
			foreach (var buildAction in _configureHostConfigActions)
			{
				buildAction(configBuilder);
			}
			_hostConfiguration = configBuilder.Build();
		}

		void BuildAppConfiguration()
		{
			var properties = new Dictionary<object, object>();
			var builderContext = new HostBuilderContext(properties); // TODO: Should get this from somewhere...

			var configBuilder = new ConfigurationBuilder();
			configBuilder.AddConfiguration(_hostConfiguration);
			foreach (var buildAction in _configureAppConfigActions)
			{
				buildAction(builderContext, configBuilder);
			}
			_appConfiguration = configBuilder.Build();

			builderContext.Configuration = _appConfiguration;
		}
	}
}