namespace Diz.PowerShell;

public class DizPowershellLogger : IDizLogger {
	private readonly IPowershellLogger _powershellLogger;

	public DizPowershellLogger(IPowershellLogger powershellLogger) {
		this._powershellLogger = powershellLogger;
	}

	public void Info(string msg) =>
		_powershellLogger.WriteObject(msg);

	public void Warn(string msg) =>
		_powershellLogger.WriteCommandDetail(msg);

	public void Error(string msg) =>
		_powershellLogger.WriteObject(msg);

	public void Debug(string msg) =>
		_powershellLogger.WriteDebug(msg);
}
