#nullable enable

using Diz.App.Common;
using System;

namespace Diz.App.Winforms;

internal static class Program {
	[STAThread]
	private static void Main(string[] args) {
		var serviceFactory = DizWinformsRegisterServices.CreateServiceFactoryAndRegisterTypes();
		DizAppCommon.StartApp(serviceFactory, args);
	}
}
