#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Diz.Core.Interfaces;

namespace Diz.Core.export;

/// <summary>
/// Exports DiztinGUIsh project data to a simplified DIZ format for Peony consumption.
/// This format focuses on the essential data needed for disassembly hints.
/// </summary>
public class SimplifiedDizExporter {
	/// <summary>
	/// Export options.
	/// </summary>
	public class ExportOptions {
		/// <summary>Whether to compress the output with gzip.</summary>
		public bool Compress { get; set; } = true;

		/// <summary>Whether to include label comments.</summary>
		public bool IncludeComments { get; set; } = true;

		/// <summary>Whether to include per-byte data types.</summary>
		public bool IncludeDataTypes { get; set; } = true;
	}

	/// <summary>
	/// Label entry in the exported DIZ.
	/// </summary>
	public class DizLabel {
		[JsonPropertyName("Name")]
		public string Name { get; set; } = "";

		[JsonPropertyName("Comment")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string? Comment { get; set; }

		[JsonPropertyName("DataType")]
		public int DataType { get; set; }
	}

	/// <summary>
	/// Root structure of the simplified DIZ file.
	/// </summary>
	public class DizProject {
		[JsonPropertyName("ProjectName")]
		public string ProjectName { get; set; } = "";

		[JsonPropertyName("RomMapMode")]
		public string RomMapMode { get; set; } = "";

		[JsonPropertyName("RomSize")]
		public int RomSize { get; set; }

		[JsonPropertyName("Labels")]
		public Dictionary<string, DizLabel> Labels { get; set; } = new();

		[JsonPropertyName("DataTypes")]
		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public int[]? DataTypes { get; set; }
	}

	/// <summary>
	/// Exports project data to simplified DIZ JSON.
	/// </summary>
	/// <param name="data">The project data to export.</param>
	/// <param name="projectName">Name of the project.</param>
	/// <param name="options">Export options.</param>
	/// <returns>JSON string.</returns>
	public string Export(IData data, string projectName, ExportOptions? options = null) {
		options ??= new ExportOptions();

		var project = new DizProject {
			ProjectName = projectName,
			RomMapMode = data.RomMapMode.ToString(),
			RomSize = data.RomBytes.Count
		};

		// Export labels
		foreach (var kvp in data.Labels.Labels) {
			var snesAddress = kvp.Key;
			var labelData = kvp.Value;

			string? comment = null;
			if (options.IncludeComments && data.Comments.TryGetValue(snesAddress, out var commentText)) {
				comment = commentText;
			}

			project.Labels[snesAddress.ToString()] = new DizLabel {
				Name = labelData.Name,
				Comment = comment,
				DataType = (int)GetDataTypeAtAddress(data, snesAddress)
			};
		}

		// Export per-byte data types
		if (options.IncludeDataTypes) {
			project.DataTypes = new int[data.RomBytes.Count];
			for (var i = 0; i < data.RomBytes.Count; i++) {
				project.DataTypes[i] = ConvertFlagTypeToDataType(data.RomBytes[i].TypeFlag);
			}
		}

		return JsonSerializer.Serialize(project, new JsonSerializerOptions {
			WriteIndented = true
		});
	}

	/// <summary>
	/// Exports project data to a simplified DIZ file.
	/// </summary>
	/// <param name="data">The project data to export.</param>
	/// <param name="path">Output file path.</param>
	/// <param name="projectName">Name of the project.</param>
	/// <param name="options">Export options.</param>
	public void ExportToFile(IData data, string path, string projectName, ExportOptions? options = null) {
		options ??= new ExportOptions();
		var json = Export(data, projectName, options);
		var jsonBytes = Encoding.UTF8.GetBytes(json);

		if (options.Compress) {
			using var output = File.Create(path);
			using var gzip = new GZipStream(output, CompressionLevel.Optimal);
			gzip.Write(jsonBytes, 0, jsonBytes.Length);
		} else {
			File.WriteAllBytes(path, jsonBytes);
		}
	}

	/// <summary>
	/// Gets the FlagType at a SNES address (converts to ROM offset).
	/// </summary>
	private static FlagType GetDataTypeAtAddress(IData data, int snesAddress) {
		// Note: This is a simplified implementation.
		// In a full implementation, you'd use the proper SNES->PC address conversion.
		var offset = snesAddress & 0xFFFF; // Simple low-word extraction
		if (offset >= 0 && offset < data.RomBytes.Count) {
			return data.RomBytes[offset].TypeFlag;
		}
		return FlagType.Unreached;
	}

	/// <summary>
	/// Converts DiztinGUIsh FlagType to simplified data type integers.
	/// </summary>
	private static int ConvertFlagTypeToDataType(FlagType flag) {
		// Map to Peony's DizDataType values
		return flag switch {
			FlagType.Unreached => 0,    // Unreached
			FlagType.Opcode => 1,       // Opcode
			FlagType.Operand => 2,      // Operand
			FlagType.Data8Bit => 3,     // Data8
			FlagType.Graphics => 4,     // Graphics
			FlagType.Music => 5,        // Music
			FlagType.Empty => 6,        // Empty
			FlagType.Data16Bit => 7,    // Data16
			FlagType.Pointer16Bit => 8, // Pointer16
			FlagType.Data24Bit => 9,    // Data24
			FlagType.Pointer24Bit => 10,// Pointer24
			FlagType.Data32Bit => 11,   // Data32
			FlagType.Pointer32Bit => 12,// Pointer32
			FlagType.Text => 13,        // Text
			_ => 0
		};
	}
}
