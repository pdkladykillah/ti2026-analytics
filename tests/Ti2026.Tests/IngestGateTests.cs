using FluentAssertions;
using Ti2026.Ingest;

namespace Ti2026.Tests;

/// <summary>
/// Cổng này sinh ra từ một lỗi thật trên production: một lời gọi POST api/ingest/run bị curl bỏ
/// ngang vẫn tiếp tục chạy phía server, rồi lời gọi tiếp theo chết ở "INSERT INTO IngestRuns"
/// sau đúng 30 giây — hết CommandTimeout vì chờ khoá ghi của SQLite. Thông báo lỗi khi đó nói
/// về DbCommand và không hề nhắc gì tới nguyên nhân thật, nên rất dễ đi sai hướng.
/// </summary>
public class IngestGateTests
{
    [Fact]
    public void Chi_mot_vong_vao_duoc_cung_luc()
    {
        using var gate = new IngestGate();

        using var first = gate.TryEnter();
        first.Should().NotBeNull();

        gate.TryEnter().Should().BeNull("vòng thứ hai phải bị từ chối NGAY, không nằm chờ khoá");
    }

    [Fact]
    public void Ra_khoi_cong_thi_nguoi_sau_vao_duoc()
    {
        using var gate = new IngestGate();

        gate.TryEnter()!.Dispose();

        using var next = gate.TryEnter();
        next.Should().NotBeNull();
    }

    [Fact]
    public void Bao_dung_trang_thai_dang_chay()
    {
        using var gate = new IngestGate();

        gate.IsRunning.Should().BeFalse();

        using (var slot = gate.TryEnter())
            gate.IsRunning.Should().BeTrue();

        gate.IsRunning.Should().BeFalse();
    }

    /// <summary>Nhả hai lần không được làm hỏng bộ đếm — nếu vỡ thì cổng mở toang vĩnh viễn.</summary>
    [Fact]
    public void Nha_hai_lan_khong_lam_hong_bo_dem()
    {
        using var gate = new IngestGate();

        var slot = gate.TryEnter();
        slot!.Dispose();
        slot.Dispose();

        using var a = gate.TryEnter();
        a.Should().NotBeNull();
        gate.TryEnter().Should().BeNull("cổng vẫn chỉ cho đúng một người vào");
    }

    /// <summary>Scheduler thì CHỜ tới lượt, vì không có ai ngồi đợi phản hồi.</summary>
    [Fact]
    public async Task Scheduler_cho_toi_luot_thay_vi_bo_qua()
    {
        using var gate = new IngestGate();

        var slot = gate.TryEnter();
        var waiting = gate.EnterAsync(CancellationToken.None);

        waiting.IsCompleted.Should().BeFalse("đang có vòng khác nên phải chờ");

        slot!.Dispose();
        using var acquired = await waiting;
        acquired.Should().NotBeNull();
    }
}
