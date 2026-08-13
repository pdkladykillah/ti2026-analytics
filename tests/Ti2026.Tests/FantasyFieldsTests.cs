using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// Năm chỉ số fantasy từng bị kết luận nhầm là "OpenDota không có". Chúng nằm trong cùng
/// payload matches/{id}, chỉ là trong ba từ điển con mà DTO chưa khai — và tìm theo tên hiển
/// thị thì không bao giờ ra, vì trong payload không hề có chữ lotus, watcher hay tormentor.
///
/// Các con số dưới đây lấy từ ván THẬT 8926048199 (patch 60, giải 20009).
/// </summary>
public class FantasyFieldsTests
{
    /// <summary>
    /// Luật ghép của Dota: 3 Healing Lotus → 1 Great, 2 Great → 1 Greater (tức 6 gốc). Đếm mỗi
    /// món bằng 1 thì người gom 6 bông rồi ghép lại bị tính bằng người nhặt đúng 1 bông — và
    /// đúng những người hay làm việc đó nhất lại là người hỗ trợ.
    /// </summary>
    [Fact]
    public void Hoa_sen_quy_ve_hoa_sen_goc_theo_luat_ghep()
    {
        FantasyFields.Lotuses(new Dictionary<string, int> { ["famango"] = 5 })
            .Should().Be(5);

        FantasyFields.Lotuses(new Dictionary<string, int> { ["great_famango"] = 1 })
            .Should().Be(3, "một Great là ba bông gốc");

        FantasyFields.Lotuses(new Dictionary<string, int> { ["greater_famango"] = 2 })
            .Should().Be(12, "một Greater là sáu bông gốc");

        // Ván thật, người ở slot 0: 1 Great + 2 Greater = 3 + 12
        FantasyFields.Lotuses(new Dictionary<string, int>
        {
            ["great_famango"] = 1, ["greater_famango"] = 2,
        }).Should().Be(15);
    }

    /// <summary>lotus_orb là món mua ở shop, KHÔNG phải hoa sen. Đây là cái bẫy của lần tìm trước.</summary>
    [Fact]
    public void Lotus_orb_khong_phai_hoa_sen()
    {
        FantasyFields.Lotuses(new Dictionary<string, int>
        {
            ["lotus_orb"] = 4, ["recipe_lotus_orb"] = 1,
        }).Should().Be(0);
    }

    [Fact]
    public void Watcher_lay_tu_ability_lamp_use()
    {
        FantasyFields.Watchers(new Dictionary<string, int> { ["ability_lamp_use"] = 9 })
            .Should().Be(9);
    }

    [Fact]
    public void Tormentor_lay_tu_npc_dota_miniboss()
    {
        FantasyFields.TormentorKills(new Dictionary<string, int> { ["npc_dota_miniboss"] = 1 })
            .Should().Be(1);
    }

    [Fact]
    public void Smoke_lay_tu_item_uses()
    {
        FantasyFields.Smokes(new Dictionary<string, int> { ["smoke_of_deceit"] = 5 })
            .Should().Be(5);
    }

    /// <summary>
    /// Đây là ranh giới quan trọng nhất trong tệp này. Từ điển null = ván OpenDota chưa parse =
    /// CHƯA BIẾT. Từ điển có mà thiếu khoá = đo được, và bằng không. Gộp hai thứ đó lại là cách
    /// chắc chắn để biến một người chưa có dữ liệu thành một người chơi tệ.
    /// </summary>
    [Fact]
    public void Tu_dien_null_tra_null_con_thieu_khoa_tra_0()
    {
        FantasyFields.Lotuses(null).Should().BeNull();
        FantasyFields.Watchers(null).Should().BeNull();
        FantasyFields.Smokes(null).Should().BeNull();
        FantasyFields.MadstoneBundles(null).Should().BeNull();
        FantasyFields.TormentorKills(null).Should().BeNull();

        var doDuocVaBangKhong = new Dictionary<string, int> { ["tango"] = 3 };
        FantasyFields.Lotuses(doDuocVaBangKhong).Should().Be(0);
        FantasyFields.Watchers(doDuocVaBangKhong).Should().Be(0);
        FantasyFields.Smokes(doDuocVaBangKhong).Should().Be(0);
        FantasyFields.TormentorKills(doDuocVaBangKhong).Should().Be(0);
    }

    /// <summary>
    /// Trường tổng hợp roshan_kills của OpenDota ĐẾM DƯ. Kiểm ba chiều với objectives làm
    /// trọng tài trên hai ván thật:
    ///
    ///   8926048199 — objectives 2 · killed 2 · roshan_kills 3
    ///   8784047386 — objectives 4 · killed 4 · roshan_kills 5
    ///
    /// Và 19/30 ván nạp gần nhất có hai nguồn lệch, tất cả cùng một chiều. Roshan là 1172
    /// điểm nên mỗi con Roshan ma cộng thẳng 1172 điểm cho người không hề chốt kill nó.
    /// </summary>
    [Fact]
    public void Roshan_dem_tu_killed_chu_khong_phai_truong_tong_hop()
    {
        FantasyFields.RoshanKills(new Dictionary<string, int> { ["npc_dota_roshan"] = 1 })
            .Should().Be(1);

        FantasyFields.RoshanKills(new Dictionary<string, int> { ["npc_dota_miniboss"] = 1 })
            .Should().Be(0, "Tormentor không phải Roshan");

        // Ván 8784047386, hero 9: trường nói 2, killed nói 1. Sự thật theo objectives là 1.
        FantasyFields.RoshanKills(new Dictionary<string, int> { ["npc_dota_roshan"] = 1 })
            .Should().Be(1, "lấy theo killed, không lấy theo trường tổng hợp");
    }
}
