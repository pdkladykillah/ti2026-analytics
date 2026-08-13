using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// OpenDota chỉ cho ba lane, không cho năm vị trí. Carry và hỗ trợ 5 đứng CÙNG lane an toàn nên
/// mang cùng lane_role — gộp chúng lại thì mọi thống kê theo vị trí đều vô nghĩa.
///
/// Quy tắc suy: trong cùng trận, cùng đội, cùng lane, ai nhiều net worth hơn là core.
/// Đã kiểm chứng trên 368 bàn draft 7.41 và năm danh sách hero đọc ra đúng như người trong nghề
/// sẽ viết — xem ghi chú ở PositionInference.
/// </summary>
public class PositionInferenceTests
{
    [Fact]
    public void Mid_khong_phu_thuoc_tai_san()
    {
        PositionInference.Infer(PositionInference.MidLane, 1).Should().Be(2);
        PositionInference.Infer(PositionInference.MidLane, 2).Should().Be(2,
            "mid đi một mình nên hạng net worth không đổi được vị trí");
    }

    [Fact]
    public void Lane_an_toan_tach_carry_khoi_ho_tro_5()
    {
        PositionInference.Infer(PositionInference.SafeLane, 1).Should().Be(1);
        PositionInference.Infer(PositionInference.SafeLane, 2).Should().Be(5);
    }

    [Fact]
    public void Lane_kho_tach_offlane_khoi_ho_tro_4()
    {
        PositionInference.Infer(PositionInference.OffLane, 1).Should().Be(3);
        PositionInference.Infer(PositionInference.OffLane, 2).Should().Be(4);
    }

    /// <summary>
    /// lane_role 4 là rừng, 0/null là OpenDota không xác định được. Cả hai đều không ánh xạ
    /// được sang vị trí 1–5, và nhét đại vào một ô sẽ làm lệch đúng cái bảng ta đang dựng.
    /// </summary>
    [Fact]
    public void Khong_xac_dinh_duoc_thi_tra_null_chu_khong_nhet_dai()
    {
        PositionInference.Infer(null, 1).Should().BeNull();
        PositionInference.Infer(0, 1).Should().BeNull();
        PositionInference.Infer(4, 1).Should().BeNull("rừng không phải một trong năm vị trí");
    }

    private record Row(int? Lane, int? NetWorth, string Who);

    [Fact]
    public void Xep_hang_theo_tai_san_trong_nhom_roi_moi_suy()
    {
        var safeLane = new[]
        {
            new Row(PositionInference.SafeLane, 8_000, "hỗ trợ"),
            new Row(PositionInference.SafeLane, 31_000, "carry"),
        };

        var result = PositionInference
            .InferGroup(safeLane, r => r.Lane, r => r.NetWorth)
            .ToDictionary(x => x.Row.Who, x => x.Position);

        result["carry"].Should().Be(1);
        result["hỗ trợ"].Should().Be(5);
    }

    /// <summary>
    /// Thiếu net worth thì xếp cuối chứ không được xếp đầu — nếu null bị coi là lớn nhất thì một
    /// ván chưa parse sẽ biến người hỗ trợ thành carry.
    /// </summary>
    [Fact]
    public void Thieu_tai_san_thi_xep_cuoi()
    {
        var rows = new[]
        {
            new Row(PositionInference.SafeLane, null, "chưa biết"),
            new Row(PositionInference.SafeLane, 12_000, "có số"),
        };

        var result = PositionInference
            .InferGroup(rows, r => r.Lane, r => r.NetWorth)
            .ToDictionary(x => x.Row.Who, x => x.Position);

        result["có số"].Should().Be(1);
        result["chưa biết"].Should().Be(5);
    }

    [Fact]
    public void Bo_qua_nguoi_khong_suy_duoc_thay_vi_bo_ca_nhom()
    {
        var mixed = new[]
        {
            new Row(PositionInference.OffLane, 20_000, "offlane"),
            new Row(4, 9_000, "đi rừng"),
        };

        var result = PositionInference
            .InferGroup(mixed, r => r.Lane, r => r.NetWorth)
            .ToList();

        result.Should().ContainSingle();
        result[0].Row.Who.Should().Be("offlane");
    }

    [Fact]
    public void Moi_vi_tri_deu_co_ten_va_mo_ta()
    {
        foreach (var p in new[] { 1, 2, 3, 4, 5 })
        {
            PositionInference.Name(p).Should().NotBeNullOrWhiteSpace();
            PositionInference.Description(p).Should().NotBeNullOrWhiteSpace();
        }
    }
}
