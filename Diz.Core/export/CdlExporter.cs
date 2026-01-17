using System;
using System.IO;
using Diz.Core.Interfaces;

namespace Diz.Core.export;

/// <summary>
/// Exports DiztinGUIsh project data to CDL (Code/Data Log) format.
/// CDL files can be consumed by Peony disassembler and emulators.
/// </summary>
public class CdlExporter {
	/// <summary>
	/// CDL output format.
	/// </summary>
	public enum CdlFormat {
		/// <summary>Mesen format: "CDL\x01" header + byte array.</summary>
		Mesen,
		/// <summary>FCEUX format: raw byte array, no header.</summary>
		FCEUX
	}

	/// <summary>
	/// Mesen CDL flags.
	/// </summary>
	[Flags]
	private enum MesenCdlFlags : byte {
		None = 0x00,
		Code = 0x01,
		Data = 0x02,
		JumpTarget = 0x04,
		SubEntryPoint = 0x08
	}

	/// <summary>
	/// FCEUX CDL flags.
	/// </summary>
	[Flags]
	private enum FceuxCdlFlags : byte {
		None = 0x00,
		Code = 0x01,
		Data = 0x02,
		Rendered = 0x04,
		IndirectData = 0x08,
		SubEntryPoint = 0x10,
		IndirectCode = 0x20,
		IndirectAccess = 0x40,
		RomBank = 0x80
	}

	/// <summary>
	/// Exports project data to CDL byte array.
	/// </summary>
	/// <param name="data">The project data to export.</param>
	/// <param name="format">The CDL format to use.</param>
	/// <returns>CDL byte array.</returns>
	public byte[] Export(IData data, CdlFormat format) {
		var romBytes = data.RomBytes;
		var romSize = romBytes.Count;
		var cdlData = new byte[romSize];

		for (var offset = 0; offset < romSize; offset++) {
			var romByte = romBytes[offset];
			var flag = romByte.TypeFlag;
			var point = romByte.Point;

			cdlData[offset] = format switch {
				CdlFormat.Mesen => ConvertToMesenFlags(flag, point),
				CdlFormat.FCEUX => ConvertToFceuxFlags(flag, point),
				_ => 0
			};
		}

		if (format == CdlFormat.Mesen) {
			// Mesen format: "CDL\x01" header + data
			var result = new byte[4 + cdlData.Length];
			result[0] = (byte)'C';
			result[1] = (byte)'D';
			result[2] = (byte)'L';
			result[3] = 0x01;
			Array.Copy(cdlData, 0, result, 4, cdlData.Length);
			return result;
		}

		return cdlData;
	}

	/// <summary>
	/// Exports project data to a CDL file.
	/// </summary>
	/// <param name="data">The project data to export.</param>
	/// <param name="path">Output file path.</param>
	/// <param name="format">The CDL format to use.</param>
	public void ExportToFile(IData data, string path, CdlFormat format) {
		var cdlBytes = Export(data, format);
		File.WriteAllBytes(path, cdlBytes);
	}

	/// <summary>
	/// Converts DiztinGUIsh FlagType to Mesen CDL flags.
	/// </summary>
	private static byte ConvertToMesenFlags(FlagType flag, InOutPoint point) {
		byte cdl = 0;

		// Code flags
		if (flag == FlagType.Opcode || flag == FlagType.Operand) {
			cdl |= (byte)MesenCdlFlags.Code;
		}

		// Data flags
		if (flag is FlagType.Data8Bit or FlagType.Data16Bit or FlagType.Data24Bit or FlagType.Data32Bit
			or FlagType.Pointer16Bit or FlagType.Pointer24Bit or FlagType.Pointer32Bit
			or FlagType.Graphics or FlagType.Music or FlagType.Text) {
			cdl |= (byte)MesenCdlFlags.Data;
		}

		// Entry points
		if ((point & InOutPoint.InPoint) != 0) {
			cdl |= (byte)MesenCdlFlags.SubEntryPoint;
		}

		// Jump targets
		if ((point & InOutPoint.OutPoint) != 0) {
			cdl |= (byte)MesenCdlFlags.JumpTarget;
		}

		return cdl;
	}

	/// <summary>
	/// Converts DiztinGUIsh FlagType to FCEUX CDL flags.
	/// </summary>
	private static byte ConvertToFceuxFlags(FlagType flag, InOutPoint point) {
		byte cdl = 0;

		// Code flags
		if (flag == FlagType.Opcode || flag == FlagType.Operand) {
			cdl |= (byte)FceuxCdlFlags.Code;
		}

		// Data flags
		if (flag is FlagType.Data8Bit or FlagType.Data16Bit or FlagType.Data24Bit or FlagType.Data32Bit
			or FlagType.Pointer16Bit or FlagType.Pointer24Bit or FlagType.Pointer32Bit
			or FlagType.Text) {
			cdl |= (byte)FceuxCdlFlags.Data;
		}

		// Graphics rendered as graphics flag
		if (flag == FlagType.Graphics) {
			cdl |= (byte)FceuxCdlFlags.Rendered;
		}

		// Entry points
		if ((point & InOutPoint.InPoint) != 0) {
			cdl |= (byte)FceuxCdlFlags.SubEntryPoint;
		}

		return cdl;
	}
}
