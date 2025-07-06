using Diz.Core;
using Diz.Core.model;
using Diz.Core.util;
using Diz.LogWriter;
using Diz.LogWriter.util;

namespace Diz.PowerShell;

public class ProjectFileAssemblyExporter : IProjectFileAssemblyExporter {
	private readonly IDizLogger _logger;

	private readonly IFilesystemService _fs;

	private readonly IProjectFileOpener _projectFileSource;

	public ProjectFileAssemblyExporter(IDizLogger logger, IProjectFileOpener projectFileSource, IFilesystemService fs) {
		this._logger = logger;
		this._projectFileSource = projectFileSource;
		this._fs = fs;
	}

	private Project? OpenProjectFile(string projectFileName) {
		var project = _projectFileSource.ReadProjectFromFile(projectFileName);
		if (project == null)
			return null;

		_logger.Debug($"Loaded project, rom is: {project.AttachedRomFilename}");
		return project;
	}

	public bool ExportAssembly(string projectFileName) {
		var project = OpenProjectFile(projectFileName);
		return project != null && ExportAssembly(project);
	}

	public bool ExportAssembly(Project project) {
		var failReason = project.LogWriterSettings.Validate(_fs);
		if (failReason != null) {
			_logger.Error($"invalid assembly build settings {failReason}");
			return false;
		}

		var lc = new LogCreator {
			Settings = project.LogWriterSettings,
			Data = new LogCreatorByteSource(project.Data),
		};

		_logger.Debug("Building....");
		var result = lc.CreateLog();

		if (!result.Success) {
			_logger.Error($"Failed to build, error was: {result.OutputStr}");
			return false;
		}

		_logger.Info("Successfully exported assembly output.");
		return true;
	}
}
