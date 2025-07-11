using System;
using Diz.Core.Interfaces;
using Diz.Core.model;
using Diz.Core.model.snes;
using ExtendedXmlSerializer;
using ExtendedXmlSerializer.Configuration;
using ExtendedXmlSerializer.ContentModel.Format;
using ExtendedXmlSerializer.ExtensionModel.Instances;
using JetBrains.Annotations;

namespace Diz.Core.serialization.xml_serializer;

public class XmlSerializerFactory : IXmlSerializerFactory
{
    private readonly IDataFactory dataFactory;
    private readonly Func<IDataFactory, SnesDataInterceptor> snesDataInterceptor;

    public XmlSerializerFactory(IDataFactory dataFactory, Func<IDataFactory, SnesDataInterceptor> snesDataInterceptor)
    {
        this.dataFactory = dataFactory;
        this.snesDataInterceptor = snesDataInterceptor;
    }

    public IConfigurationContainer GetSerializer([CanBeNull] RomBytesOutputFormatSettings romBytesOutputFormat)
    {
        var romBytesSerializer = new RomBytesSerializer
        {
            FormatSettings = romBytesOutputFormat
        };
        
        return new ConfigurationContainer()

            .WithDefaultMonitor(new SerializationMonitor())

            .Type<Project>()
            
            // dizprefs: we'll save these manually so they're in a different file
            .Member(x => x.ProjectUserSettings).Ignore() 

            .Type<RomBytes>()
            .Register().Serializer().Using(romBytesSerializer)

            .Type<Data>()
            .WithInterceptor(snesDataInterceptor(dataFactory))
            .Member(x => x.LabelsSerialization)

            .Name("Labels")
            .UseOptimizedNamespaces()
            .UseAutoFormatting()

#if DIZ_3_BRANCH
                .EnableReferences()
#endif

            .EnableImplicitTyping(typeof(Data))

            .Type<Label>()

#if DIZ_3_BRANCH
                .Name("L")
                .Member(x => x.Comment).Name("Cmt").EmitWhen(text => !string.IsNullOrEmpty(text))
                .Member(x => x.Name).Name("V").EmitWhen(text => !string.IsNullOrEmpty(text))
#endif
            .EnableImplicitTyping()

            .Type<IAnnotationLabel>()
            .WithInterceptor(AnnotationLabelInterceptor.Default);
    }

    /// <summary>
    /// Generic serialization monitor. Use this to hook into key events, debug, report progress, etc.
    /// </summary>
    private class SerializationMonitor : ISerializationMonitor
    {
        public void OnSerializing(IFormatWriter writer, object instance)
        {
                
        }

        public void OnSerialized(IFormatWriter writer, object instance)
        {
                
        }

        public void OnDeserializing(IFormatReader reader, Type instanceType)
        {
                
        }

        public void OnActivating(IFormatReader reader, Type instanceType)
        {
                
        }

        public void OnActivated(object instance)
        {
                
        }

        public void OnDeserialized(IFormatReader reader, object instance)
        {
                
        }
    }

    public abstract class GenericInterceptor<T> : ISerializationInterceptor<T>
    {
        public virtual T Serializing(IFormatWriter writer, T instance) => instance;
        public virtual T Deserialized(IFormatReader reader, T instance) => instance;
        public abstract T Activating(Type instanceType);
    }


    /// <summary>
    /// Important migration.  Label was changed to IAnnotationLabel, and existing serialized data
    /// doesn't know to create Labels when it sees IAnnotationLabel (because "exs:type" attribute is omitted).
    ///
    /// If this is hit, it means we need to manually step in and specify the type of Label, or else it'll crash.
    /// </summary>
    public sealed class AnnotationLabelInterceptor : GenericInterceptor<IAnnotationLabel>
    {
        public static AnnotationLabelInterceptor Default { get; } = new();

        // critical note:
        // activate type of Label anytime we see IAnnotationLabel.
        public override IAnnotationLabel Activating(Type instanceType) => new Label();
    }
    
    public sealed class SnesDataInterceptor : GenericInterceptor<Data>
    {
        private readonly IDataFactory dataFactory;
        public SnesDataInterceptor(IDataFactory dataFactory)
        {
            this.dataFactory = dataFactory;
        }

        // TODO: eventually make this IData not Data
        public override Data Activating(Type instanceType) =>
            dataFactory.Create();
    }
}