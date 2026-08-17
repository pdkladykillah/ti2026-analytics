namespace Ti2026.Data.Entities;

/// <summary>
/// Bảng điểm đầy đủ của một ván: MỘT DÒNG MỖI NGƯỜI, cả mười người hai phe.
///
/// VÌ SAO CẦN BẢNG RIÊNG, khi đã có TrackedPlayerMatches. Vì hai bảng trả lời hai câu khác nhau.
/// TrackedPlayerMatches là hồ sơ CỦA NGƯỜI ĐƯỢC THEO DÕI qua thời gian — mỗi ván một dòng, 55
/// cột, dùng để dựng xu hướng. Bảng này là ảnh chụp MỘT VÁN với cả mười người, để đọc lại đúng
/// ván đó như mở bảng điểm.
///
/// VÌ SAO KHÔNG DÙNG LẠI MatchPlayers. Bảng đó phục vụ 16 đội dự giải, và MatchDetailIngester
/// quét chính nó để biết ván nào còn thiếu chi tiết. Trộn ván pub của người dùng vào đó sẽ làm
/// bộ đếm "còn bao nhiêu ván cần nạp" mất nghĩa — mà đó là chỉ báo duy nhất cho biết việc nạp
/// bù có đang tắc hay không.
///
/// NẠP THEO YÊU CẦU, KHÔNG NẠP SẴN. Nạp sẵn cả 9.900 ván của hai tài khoản tốn 9.900 lời gọi,
/// tức năm ngày hạn mức miễn phí, cho một thứ mà phần lớn sẽ không ai mở ra xem. Nạp khi bấm thì
/// chi phí bằng đúng số ván thật sự được xem, và lần xem thứ hai là miễn phí vì đã có ở đây.
/// </summary>
public class TrackedMatchBoard
{
    public long Id { get; set; }

    public long MatchId { get; set; }

    /// <summary>player_slot của OpenDota: 0–4 là Radiant, 128–132 là Dire.</summary>
    public int PlayerSlot { get; set; }

    public bool IsRadiant { get; set; }

    /// <summary>null với người đặt hồ sơ ở chế độ riêng tư — chuyện thường ở ván pub.</summary>
    public long? AccountId { get; set; }

    public string? PersonaName { get; set; }

    public int HeroId { get; set; }

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }

    public int? Level { get; set; }
    public int? NetWorth { get; set; }
    public int? LastHits { get; set; }
    public int? Denies { get; set; }
    public int GoldPerMin { get; set; }
    public int XpPerMin { get; set; }
    public int? HeroDamage { get; set; }
    public int? TowerDamage { get; set; }
    public int? HeroHealing { get; set; }

    /// <summary>Nhóm chơi chung. Cùng số nghĩa là cùng party — dùng để đánh dấu đội bạn bè.</summary>
    public int? PartyId { get; set; }

    public int? RankTier { get; set; }

    /// <summary>
    /// Sáu ô đồ cuối ván, đã quy 0 về null.
    ///
    /// Lưu thành sáu cột chứ không một chuỗi: giao diện cần từng ô để tra ảnh, và một chuỗi
    /// "1,29,0,63,0,0" thì mọi nơi đọc nó phải tự tách — tức mỗi nơi một cách tách.
    /// </summary>
    public int? Item0 { get; set; }
    public int? Item1 { get; set; }
    public int? Item2 { get; set; }
    public int? Item3 { get; set; }
    public int? Item4 { get; set; }
    public int? Item5 { get; set; }

    /// <summary>Lúc nạp bảng điểm này về. Dùng để biết nó đã có sẵn, không phải gọi lại mạng.</summary>
    public DateTime FetchedAt { get; set; }
}
