#nullable enable

namespace Diz.PowerShell;

public interface IPowershellLogger {
	void WriteObject(object objectToSend);

	void WriteDebug(string text);

	void WriteCommandDetail(string text);
}
