﻿using Microsoft.Maui.Handlers;
using Microsoft.Maui.LifecycleEvents;

namespace Microsoft.Maui.Platform
{
	public static class ApplicationExtensions
	{
		public static void CreateNativeWindow(this UI.Xaml.Application nativeApplication, IApplication application, UI.Xaml.LaunchActivatedEventArgs? args) =>
			nativeApplication.CreateNativeWindow(application, new OpenWindowRequest(LaunchArgs: args));

		public static void CreateNativeWindow(this UI.Xaml.Application nativeApplication, IApplication application, OpenWindowRequest? args)
		{
			if (application.Handler?.MauiContext is not IMauiContext applicationContext)
				return;

			var winuiWndow = new MauiWinUIWindow();

			var mauiContext = applicationContext!.MakeScoped(winuiWndow);

			applicationContext.Services.InvokeLifecycleEvents<WindowsLifecycle.OnMauiContextCreated>(del => del(mauiContext));

			var activationState = args?.State is not null
				? new ActivationState(mauiContext, args.State)
				: new ActivationState(mauiContext, args?.LaunchArgs);

			var window = application.CreateWindow(activationState);

			winuiWndow.SetWindowHandler(window, mauiContext);

			applicationContext.Services.InvokeLifecycleEvents<WindowsLifecycle.OnWindowCreated>(del => del(winuiWndow));

			winuiWndow.Activate();
		}
	}
}