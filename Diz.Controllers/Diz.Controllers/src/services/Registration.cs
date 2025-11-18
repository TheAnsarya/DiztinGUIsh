// using Diz.Controllers.controllers;

using Diz.Controllers.controllers;
using Diz.Controllers.interfaces;
using Diz.Controllers.services;
using Diz.Controllers.util;
using Diz.Core.Interfaces;
using JetBrains.Annotations;
using LightInject;

namespace Diz.Controllers.services;

[UsedImplicitly]
public class DizControllersCompositionRoot : ICompositionRoot
{
    public void Compose(IServiceRegistry serviceRegistry)
    {
        serviceRegistry.Register<IProjectController, ProjectController>("ProjectController");
        serviceRegistry.Register<ILogCreatorSettingsEditorController, LogCreatorSettingsEditorController>("AssemblyExporterSettingsController");
        serviceRegistry.Register<IImportRomDialogController, ImportRomDialogController>("ImportRomDialogController");
        serviceRegistry.Register<ILargeFilesReaderController, LargeFilesReader>("LargeFileReaderProgressController");
        
        // Mesen2 integration controller
        serviceRegistry.Register<IMesen2IntegrationController, Mesen2IntegrationController>(new PerContainerLifetime());
        
        serviceRegistry.EnableAutoFactories();
        serviceRegistry.RegisterAutoFactory<IControllerFactory>();

        serviceRegistry.Register<IDizDocument, DizDocument>();

        // sorry this is all a huge WIP mess, cleanup incoming soon.

        // serviceRegistry.Register<int, int, IReadOnlySnesRom, IMarkManyController>(
        //     (factory, offset, whichIndex, data) =>
        //     {
        //          // TODO: update this with updated controller from Diz 2.0 branch.
        //          // I think that means kill 'whichIndex', use the new format that doesn't rely on it.
        //         var view = factory.GetInstance<IMarkManyView>();
        //         var markManyController = new MarkManyController(offset, whichIndex, data, view);
        //         markManyController.MarkManyView.Controller = markManyController;
        //         return markManyController;
        //     });

        // TODO: might be able to make some of these register using
        // "open generics" to be more flexible.

#if DIZ_3_BRANCH
            serviceRegistry.Register(
                typeof(IDataController), 
                typeof(RomByteDataBindingController<IGridRow<ByteEntry>>)
            );

            serviceRegistry.Register<IDataController, RomByteDataBindingGridController>();

            serviceRegistry.Register(
                typeof(IBytesGridDataController<,>),
                typeof(RomByteDataBindingController<>)
            );
            
            serviceRegistry.Register(
                typeof(IBytesGridDataController<IDataGridRow,ByteEntry>),
                typeof(RomByteDataBindingGridController)
            );
            
            serviceRegistry.Register<IStartFormController, StartFormController>();
            
            serviceRegistry.Register<IMainFormController, MainFormController>();
            
            serviceRegistry.Register<IProjectLoader, ProjectFileLoader>();
            serviceRegistry.Decorate(
                typeof(IProjectLoader), 
                typeof(ProjectLoaderWithSampleDataDecorator));

            serviceRegistry.Register<IProjectsManager, ProjectsManager>();
            
            serviceRegistry.RegisterSingleton<ISampleProjectLoader, ProjectsManager>("SampleProjectLoader");
#endif
    }
}