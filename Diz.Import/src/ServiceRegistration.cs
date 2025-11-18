using Diz.Core.Interfaces;
using Diz.Core.Mesen2;
using LightInject;

namespace Diz.Import;

public class DizImportServiceRegistration : ICompositionRoot
{
    public void Compose(IServiceRegistry serviceRegistry)
    {
        // Register Mesen2 streaming services
        serviceRegistry.Register<IMesen2StreamingClient, Mesen2StreamingClient>(new PerContainerLifetime());
        serviceRegistry.Register<IMesen2StreamingClientFactory, Mesen2StreamingClientFactory>();
        
        // Register Mesen2 configuration service
        serviceRegistry.Register<IMesen2Configuration, Mesen2Configuration>();
    }
}