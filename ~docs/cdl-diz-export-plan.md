# DiztinGUIsh CDL/DIZ Export Integration Plan

## Overview

Add export functionality to DiztinGUIsh to generate CDL and DIZ files that can be consumed by Peony for enhanced disassembly.

## Current State

### DiztinGUIsh Import (Existing)
- **BizHawk CDL Import**: `Diz.Import/src/bizhawk/BizHawkCdlImporter.cs`
- **Mesen CDL Import**: Via live streaming in `Diz.Import/src/mesen/tracelog/`
- **Label Import**: CSV and symbol file support

### DiztinGUIsh Export (Existing)
- **Assembly Export**: `Diz.LogWriter/` - generates .asm files
- **Project Save**: `.diz` project files (gzip JSON)

### Missing Functionality
- **CDL Export**: Generate CDL files from DiztinGUIsh project data
- **Simplified DIZ Export**: Export optimized DIZ for Peony consumption

## Implementation Plan

### Phase 1: CDL Export

#### File: `Diz.Core/export/CdlExporter.cs`

```csharp
namespace Diz.Core.export;

public class CdlExporter {
	public enum CdlFormat { Mesen, FCEUX }
	
	public byte[] Export(ISnesData data, CdlFormat format) {
		// Convert FlagType to CDL flags
		// Mesen: 4-byte header "CDL\x01" + data
		// FCEUX: raw data
	}
	
	public void ExportToFile(ISnesData data, string path, CdlFormat format) {
		File.WriteAllBytes(path, Export(data, format));
	}
}
```

#### FlagType → CDL Mapping

| DiztinGUIsh FlagType | CDL Code Flag | CDL Data Flag | Notes |
|---------------------|--------------|---------------|-------|
| Unreached (0x00) | 0 | 0 | Not logged |
| Opcode (0x10) | 1 | 0 | Code executed |
| Operand (0x11) | 1 | 0 | Part of code |
| Data8Bit (0x20) | 0 | 1 | Data read |
| Graphics (0x21) | 0 | 1 | Data read |
| Music (0x22) | 0 | 1 | Data read |
| Text (0x60) | 0 | 1 | Data read |
| Pointer* | 0 | 1 | Data read |

### Phase 2: Simplified DIZ Export

#### File: `Diz.Core/export/SimplifiedDizExporter.cs`

Export a simplified JSON format optimized for Peony:

```json
{
	"ProjectName": "MyProject",
	"RomMapMode": "LoRom",
	"RomSize": 1048576,
	"Labels": {
		"32768": { "Name": "reset", "Comment": "Reset vector", "DataType": 1 }
	},
	"DataTypes": [0, 1, 2, 2, 0, 3, 3, ...]
}
```

### Phase 3: UI Integration

#### Menu Items
- File → Export → CDL (Mesen format)
- File → Export → CDL (FCEUX format)
- File → Export → Simplified DIZ

#### Export Dialog
- Format selection
- Output path
- Options (compress, include labels, etc.)

## Testing

### Unit Tests
- `CdlExporter` roundtrip: Export → Import should preserve data
- Format compatibility with Mesen and FCEUX
- Large file performance

### Integration Tests
- Export from DiztinGUIsh → Import in Peony
- Verify labels and data types transfer correctly

## Timeline

1. **Week 1**: Core `CdlExporter` implementation
2. **Week 2**: `SimplifiedDizExporter` implementation
3. **Week 3**: UI integration and dialogs
4. **Week 4**: Testing and documentation

## Related Files

### DiztinGUIsh
- `Diz.Core.Interfaces/Enums.cs` - FlagType enum
- `Diz.Core/model/ROMByte.cs` - Per-byte data
- `Diz.Core/model/Project.cs` - Project structure
- `Diz.Import/src/bizhawk/BizHawkCdlImporter.cs` - CDL import reference

### Peony
- `Peony.Core/CdlLoader.cs` - CDL import
- `Peony.Core/DizLoader.cs` - DIZ import
- `Peony.Core/SymbolLoader.cs` - Symbol integration

### Poppy
- `Poppy.Core/CodeGen/CdlGenerator.cs` - CDL generation reference
- `Poppy.Core/CodeGen/DizGenerator.cs` - DIZ generation reference
