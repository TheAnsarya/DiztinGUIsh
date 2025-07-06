using Diz.Core.util;
using LightInject;
using System.Management.Automation;

namespace Diz.PowerShell;

public abstract class ServiceContainerCmdletBase : PSCmdlet {
	protected IServiceContainer? ServiceContainer { get; private set; }

	protected override void BeginProcessing() {
		ServiceContainer ??= DizServiceProvider.CreateServiceContainer();
	}

	protected override void EndProcessing() {
		ServiceContainer?.Dispose();
		ServiceContainer = null;
	}
}
