namespace Ti2026.Ingest.Analytics;

/// <summary>Vai trò của một ván, kèm mức tin cậy của chính kết luận đó.</summary>
/// <param name="Code">pos1 | pos2 | pos3 | pos4 | pos5 | core | support | khong-biet</param>
/// <param name="Source">replay = nhãn thật; doi-hinh = suy từ thứ hạng trong đội; khong = chịu</param>
public readonly record struct RoleVerdict(string Code, string Label, string Source, bool IsExact);

/// <summary>
/// Xác định vai trò của một ván, và NÓI RÕ kết luận đó chắc tới đâu.
///
/// VÌ SAO KHÔNG DÙNG MỨC FARM. Đây là điểm người dùng phản bác và họ đúng. Đo trên tài khoản
/// thật, mức farm theo lane THẬT:
///
///   safe (pos1)  299 last hit   543 GPM   678 XPM
///   mid  (pos2)  345 last hit   667 GPM   915 XPM
///   off  (pos3)  282 last hit   533 GPM   707 XPM
///
/// Ba lane farm gần như bằng nhau. Người chơi offlane tốt farm ngang carry, nên một quy tắc
/// dựa vào farm tuyệt đối sẽ xếp nhầm họ CÓ HỆ THỐNG — sai lệch đều một chiều, thứ khó phát
/// hiện nhất vì bảng kết quả vẫn trông hợp lý.
///
/// VÌ SAO THỨ HẠNG TRONG ĐỘI CHỈ ĐỦ MỘT NỬA. Đã kiểm trên 36 ván có nhãn thật: hạng XPM trong
/// đội của người đi mid là {1:4, 2:6, 4:1}, của safe là {1:1, 2:4, 3:1, 4:2}, của off là
/// {1:2, 2:4, 3:6, 4:3, 5:2}. Trung bình có tách nhưng phân bố chồng nhau nặng — đủ để phân
/// biệt CORE với SUPPORT, không đủ để nói mid hay offlane.
///
/// BA CÁM DỖ ĐÃ CÂN NHẮC VÀ LOẠI:
///
/// • Tỷ lệ XPM/GPM để dò mid. Sai vì tỷ lệ này KHÔNG đơn điệu theo lane: người hỗ trợ mới có
///   tỷ lệ cao nhất trận (hút XP lane mà gần như không ăn lính), cao hơn cả mid. Luật "tỷ lệ
///   cao là mid" sẽ gán pos5 thành pos2.
///
/// • Hạng level trong đội. Level cuối ván là hàm đơn điệu của XPM nhân thời lượng, nên hạng
///   level chỉ là hạng XPM bị lượng tử hoá và chặn trần ở level 30 — thêm nhiễu, không thêm tin.
///
/// • Sát thương trụ. Nó đo THẮNG/THUA và core/hỗ trợ, không đo lane: trong ván thua sớm thì mọi
///   core đều gần 0 và tín hiệu tắt hẳn ở gần nửa số ván.
///
/// Lý do sâu xa khiến cả ba đều hỏng: lane chỉ kéo dài khoảng 10 phút trong một ván trung vị
/// 38 phút. Mọi chỉ số trung bình cả trận đều đã pha loãng sự kiện đó tới mức không khôi phục
/// được — đúng như phân bố chồng nhau đo được ở trên.
///
/// NÊN: ván đã parse thì lấy nhãn thật; ván chưa parse thì chỉ dám nói core/support và phải
/// khai đó là suy luận. Không bao giờ đoán lane từ chỉ số.
///
/// GIỚI HẠN CỦA CHÍNH NHÃN THẬT, phải nói ra: lane_role của OpenDota cũng là suy đoán — từ VỊ
/// TRÍ ĐỨNG trong 10 phút đầu, đọc từ replay. Nó sai ở ván đổi lane hoặc pos4 roam sớm. Nhưng
/// nó suy từ toạ độ thật chứ không từ chỉ số tổng kết, nên vẫn hơn hẳn mọi cách suy gián tiếp.
/// </summary>
public static class RoleResolver
{
    /// <summary>Hạng net worth từ mức này trở xuống trong đội thì là support.</summary>
    public const int SupportFarmRank = 4;

    public static RoleVerdict Resolve(int? laneRole, int? teamFarmRank)
    {
        // 1. NHÃN THẬT từ replay. lane_role cho biết LANE; hạng farm trong đội tách tiếp core
        //    với support ở cùng lane đó — safelane có cả pos1 lẫn pos5, offlane có cả pos3 lẫn
        //    pos4, nên chỉ mình lane_role vẫn chưa ra được vị trí.
        if (laneRole is int lane && lane is >= 1 and <= 3)
        {
            var support = teamFarmRank >= SupportFarmRank;

            return lane switch
            {
                2 => new RoleVerdict("pos2", "Mid (pos 2)", "replay", true),
                1 => support
                    ? new RoleVerdict("pos5", "Hard support (pos 5)", "replay", true)
                    : new RoleVerdict("pos1", "Carry (pos 1)", "replay", true),
                _ => support
                    ? new RoleVerdict("pos4", "Support cơ động (pos 4)", "replay", true)
                    : new RoleVerdict("pos3", "Offlane (pos 3)", "replay", true),
            };
        }

        // 2. Chưa parse: chỉ nói được core hay support, và nói rõ là suy luận.
        if (teamFarmRank is int rank && rank is >= 1 and <= 5)
        {
            return rank >= SupportFarmRank
                ? new RoleVerdict("support", "Hỗ trợ (suy từ đội hình)", "doi-hinh", false)
                : new RoleVerdict("core", "Core (suy từ đội hình)", "doi-hinh", false);
        }

        // 3. Không có bối cảnh đội thì CHỊU. Đoán bằng mức farm ở đây chính là cái sai đã loại.
        return new RoleVerdict("khong-biet", "Chưa xác định", "khong", false);
    }

    /// <summary>
    /// Bảng chéo giữa hạng farm trong đội và lane thật — thứ DUY NHẤT kiểm được, và cố ý KHÔNG
    /// gọi là "độ chính xác".
    ///
    /// VÌ SAO KHÔNG ĐO ĐƯỢC ĐỘ CHÍNH XÁC. Bản đầu của hàm này so kết luận "core hay hỗ trợ" suy
    /// từ hạng farm với một "nhãn thật" mà chính nó cũng định nghĩa bằng hạng farm — lặp vòng,
    /// và cho ra 98,5% trong khi thực chất chỉ kiểm được đúng một ca (ván mid có hạng farm ≥4).
    /// Suýt nữa thì con số bịa đó lên thẳng trang.
    ///
    /// Sự thật: OpenDota KHÔNG có nhãn độc lập cho vị trí 1..5. lane_role chỉ cho biết LANE, mà
    /// safelane chứa cả pos1 lẫn pos5. Việc chia core/hỗ trợ theo hạng farm đứng được bằng CƠ
    /// CHẾ — pos5 theo định nghĩa là người farm ít nhất đội — chứ không bằng phép đo.
    ///
    /// Nên trang phải nói đúng thế: nêu bảng chéo để người đọc tự thấy, và không gắn cho nó một
    /// con số phần trăm mà nó không đỡ nổi.
    /// </summary>
    public static Dictionary<int, Dictionary<int, int>> CrossTab(
        IEnumerable<(int? LaneRole, int? TeamFarmRank)> labelled)
    {
        var table = new Dictionary<int, Dictionary<int, int>>();

        foreach (var (lane, rank) in labelled)
        {
            if (lane is not int l || l is < 1 or > 3) continue;
            if (rank is not int r || r is < 1 or > 5) continue;

            if (!table.TryGetValue(r, out var row))
                table[r] = row = new Dictionary<int, int>();

            row[l] = row.GetValueOrDefault(l) + 1;
        }

        return table;
    }
}
