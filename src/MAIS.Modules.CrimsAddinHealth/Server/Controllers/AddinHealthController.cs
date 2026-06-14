using MAIS.Modules.CrimsAddinHealth.Hubs;
using MAIS.Modules.CrimsAddinHealth.Models;
using MAIS.Modules.CrimsAddinHealth.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace MAIS.Modules.CrimsAddinHealth.Server.Controllers;

[ApiController]
[Route("api/v1/addin-health")]
[Produces("application/json")]
public sealed class AddinHealthController : ControllerBase
{
    private readonly ManifestService _manifest;
    private readonly UpdateQueue     _queue;
    private readonly AuditLogger     _audit;
    private readonly IAddinHealthMessageHandler _handler;
    private readonly ILogger<AddinHealthController> _logger;

    public AddinHealthController(
        ManifestService manifest,
        UpdateQueue queue,
        AuditLogger audit,
        IAddinHealthMessageHandler handler,
        ILogger<AddinHealthController> logger)
    {
        _manifest = manifest;
        _queue    = queue;
        _audit    = audit;
        _handler  = handler;
        _logger   = logger;
    }

    /// <summary>Returns the current addin manifest (list of expected DLL versions).</summary>
    [HttpGet("manifest")]
    public IActionResult GetManifest() => Ok(_manifest.GetManifest());

    /// <summary>Forces a manifest rebuild from the repository folder.</summary>
    [HttpPost("manifest/refresh")]
    public async Task<IActionResult> RefreshManifest(CancellationToken ct)
    {
        await _manifest.RefreshAsync(ct);
        return Ok(new { message = "Manifest refreshed.", entries = _manifest.GetManifest().Entries.Count });
    }

    /// <summary>Returns all waiting update requests (pending approval).</summary>
    [HttpGet("pending")]
    public IActionResult GetPending()
    {
        var pending = _queue.GetPending()
            .Select(e => e.Request)
            .ToList();
        return Ok(pending);
    }

    /// <summary>Returns the current state of the update queue (waiting + scheduled).</summary>
    [HttpGet("queue")]
    public IActionResult GetQueue()
    {
        return Ok(new
        {
            waiting   = _queue.GetPending(),
            scheduled = _queue.GetScheduled()
        });
    }

    /// <summary>Returns audit records from the last N days (default 7).</summary>
    [HttpGet("audit")]
    public async Task<IActionResult> GetAudit([FromQuery] int days = 7, CancellationToken ct = default)
    {
        var records = await _audit.GetRecentAsync(days, ct);
        return Ok(records);
    }

    /// <summary>Receives a scan result from a client machine.</summary>
    [HttpPost("scan-results")]
    public async Task<IActionResult> ReceiveScanResult([FromBody] ScanResult result, CancellationToken ct)
    {
        if (result is null) return BadRequest("Scan result is required.");
        await _handler.HandleScanResultAsync(result, ct);
        return Accepted();
    }

    /// <summary>Receives CRIMS process status from a client machine.</summary>
    [HttpPost("crims-status/{queueId}")]
    public async Task<IActionResult> ReceiveCrimsStatus(string queueId, [FromBody] CrimsProcessStatus status, CancellationToken ct)
    {
        await _handler.HandleCrimsStatusAsync(queueId, status, ct);
        return Accepted();
    }

    /// <summary>Receives update outcome from a client machine.</summary>
    [HttpPost("update-outcome/{queueId}")]
    public async Task<IActionResult> ReceiveUpdateOutcome(string queueId, [FromBody] UpdateOutcome outcome, CancellationToken ct)
    {
        await _handler.HandleUpdateOutcomeAsync(queueId, outcome, ct);
        return Accepted();
    }

    /// <summary>Receives an approval decision from a support/admin sidebar.</summary>
    [HttpPost("approvals")]
    public async Task<IActionResult> ReceiveApproval([FromBody] UpdateApproval approval, CancellationToken ct)
    {
        if (approval is null) return BadRequest("Approval is required.");
        await _handler.HandleApprovalAsync(approval, ct);
        return Accepted();
    }
}