namespace Ti2026.Data.Entities;

/// <summary>
/// Một ván của người được theo dõi, lưu THÔ từng ván.
///
/// Không gộp sẵn thành trung bình lúc nạp: gộp sớm thì mất khả năng hỏi lại "ba tháng gần đây",
/// "chỉ hero đi mid", "chỉ ván solo" về sau — mà đó đúng là những câu hỏi một trang theo dõi
/// bản thân sẽ phải trả lời.
///
/// Toàn bộ lấy từ một lời gọi players/{id}/matches, không cần match detail từng ván. Đó là lý
/// do chọn đúng bộ cột này: chúng là những gì endpoint đó thực sự trả về.
/// </summary>
public class TrackedPlayerMatch
{
    public long Id { get; set; }

    public int TrackedPlayerId { get; set; }
    public TrackedPlayer? TrackedPlayer { get; set; }

    public long MatchId { get; set; }

    public int HeroId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime StartTime { get; set; }

    public int DurationSeconds { get; set; }

    public bool Won { get; set; }

    /// <summary>
    /// true = phe Radiant, suy từ player_slot &lt; 128.
    ///
    /// Lưu hẳn thành cột chứ không suy lại lúc đọc: ta không lưu player_slot, nên nếu không có
    /// cột này thì phần phân tích lệch bên sân không có gì để dựa vào — và cám dỗ lúc đó là
    /// bịa ra một quy tắc trông hợp lý. Trên tài khoản thật, chênh lệch Radiant/Dire là 7,7
    /// điểm phần trăm qua gần 6.000 ván, tức hoàn toàn có thật và đáng đo.
    /// </summary>
    public bool IsRadiant { get; set; }

    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Assists { get; set; }

    public int? GoldPerMin { get; set; }
    public int? XpPerMin { get; set; }
    public int? LastHits { get; set; }
    public int? Denies { get; set; }
    public int? HeroDamage { get; set; }
    public int? TowerDamage { get; set; }
    public int? HeroHealing { get; set; }

    /// <summary>
    /// lane_role của OpenDota: 1 safe, 2 mid, 3 offlane, 4 rừng. null = ván chưa được parse.
    ///
    /// Đây là NHÃN THẬT, đọc từ replay, không phải suy đoán. Nó chỉ có sau khi ván được parse —
    /// OpenDota KHÔNG tự parse ván pub (đo thật: 0/25 ván gần nhất có sẵn), phải chủ động xin.
    /// Xem <see cref="ParseRequestedAt"/>.
    /// </summary>
    public int? LaneRole { get; set; }

    // ---------- Bối cảnh cả đội, lấy từ matches/{id} ----------
    //
    // VÌ SAO CẦN. Mức farm TUYỆT ĐỐI không nói lên vai trò: đo trên tài khoản thật thì ván
    // offlane có 282 last hit còn ván safelane có 299 — gần như bằng nhau. Người chơi giỏi ở
    // offlane vẫn farm ngang carry, và một bộ luật dựa vào farm tuyệt đối sẽ xếp nhầm họ một
    // cách CÓ HỆ THỐNG.
    //
    // Thứ hạng TRONG ĐỘI thì nói được nhiều hơn: khoảng cách giữa core và support là rất lớn
    // (net worth 20k so với 8k trong ván mẫu), nên tách core/support rất chắc. Nhưng tách
    // mid/safe/off thì KHÔNG — đã đo trên 36 ván có nhãn thật và phân bố chồng nhau nặng.

    /// <summary>Net worth cuối ván.</summary>
    public int? NetWorth { get; set; }

    public int? Level { get; set; }

    /// <summary>Hạng net worth trong 5 người CÙNG ĐỘI, 1 = giàu nhất.</summary>
    public int? TeamFarmRank { get; set; }

    /// <summary>Hạng XPM trong 5 người cùng đội, 1 = cao nhất. Người đi mid thường đứng đầu.</summary>
    public int? TeamXpmRank { get; set; }

    /// <summary>UTC, lần lấy matches/{id}. null = chưa có bối cảnh đội.</summary>
    public DateTime? DetailFetchedAt { get; set; }

    /// <summary>
    /// UTC, lần đã xin OpenDota parse ván này. null = chưa xin.
    ///
    /// Replay của Valve hết hạn sau khoảng 2 tháng — đã đo: ván 61 ngày tuổi parse được, ván 70
    /// ngày thì không. Nên cột này cũng là cách để KHÔNG xin lại mãi những ván đã quá hạn.
    /// </summary>
    public DateTime? ParseRequestedAt { get; set; }

    /// <summary>7 = xếp hạng. Cần để tách ván nghiêm túc khỏi Turbo và chế độ vui.</summary>
    public int? LobbyType { get; set; }

    /// <summary>22 = All Pick xếp hạng.</summary>
    public int? GameMode { get; set; }

    /// <summary>Số người trong nhóm. 1 = đi một mình. null = OpenDota không biết.</summary>
    public int? PartySize { get; set; }

    /// <summary>
    /// Rank trung bình của ván theo ước lượng OpenDota (10 = Herald 1 … 80 = Immortal).
    /// Đây là thước đo ĐỘ KHÓ của ván, và là thứ cho phép nói "thắng ở mức nào" thay vì chỉ
    /// "thắng bao nhiêu".
    /// </summary>
    public int? AverageRank { get; set; }

    // ---------- Phân vị so với mọi người chơi cùng hero, lấy từ matches/{id} ----------
    //
    // VÌ SAO ĐÁNG LƯU. "600 GPM" không nói được gì nếu không biết 600 là nhiều hay ít TRÊN HERO
    // ĐÓ — 600 với Anti-Mage là kém, với Crystal Maiden là phi thường. Phân vị của OpenDota giải
    // đúng chuyện đó: nó so với mọi người chơi cùng hero. Có ở cả ván chưa parse, nên phủ 100%
    // lịch sử chứ không phải 6% như lane_role.
    //
    // 0..100. null nghĩa là KHÔNG ĐO ĐƯỢC, và có hai đường dẫn tới null:
    //   • ván chưa lấy chi tiết, hoặc OpenDota không trả benchmark cho chỉ số đó;
    //   • giá trị thật bằng 0 — lúc đó phân vị là rác, xem OpenDotaMatchPlayer.Benchmarks.
    // Cả hai đều phải là null chứ không phải 0: 0 nghĩa là "kém hơn tất cả", khác hẳn "chưa biết".

    public int? PctGpm { get; set; }
    public int? PctXpm { get; set; }
    public int? PctLastHits { get; set; }
    public int? PctDenies { get; set; }
    public int? PctKills { get; set; }

    /// <summary>Phân vị CAO = chết NHIỀU = tệ. Đảo chiều trước khi gộp vào bất kỳ điểm tổng nào.</summary>
    public int? PctDeaths { get; set; }

    public int? PctAssists { get; set; }
    public int? PctHeroDamage { get; set; }
    public int? PctHeroHealing { get; set; }
    public int? PctTowerDamage { get; set; }

    // ---------- Kinh tế CỦA CẢ HAI PHE, để đo hiệu quả của cái chết ----------
    //
    // VÌ SAO CẦN. Câu hỏi "cái chết của tôi có tạo ra khoảng trống cho đồng đội không" KHÔNG trả
    // lời được bằng chỉ số của riêng người chết. Đã thử dùng số hỗ trợ làm proxy và người dùng
    // bác đúng: hỗ trợ chỉ ghi nhận việc CÓ MẶT lúc hạ gục, còn một cái chết mua thời gian cho
    // đồng đội đi farm hay đẩy trụ thì không để lại dấu vết nào trong đó.
    //
    // Thứ đo được điều đó là kinh tế của bốn người kia trong CHÍNH ván ấy. Nếu lối chơi hi sinh
    // có hiệu quả thì những ván ta chết nhiều phải là những ván đồng đội giàu hơn thường lệ.
    //
    // Kèm kinh tế phe địch vì thiếu nó thì không phân biệt được "đồng đội giàu vì ta tạo được
    // khoảng trống" với "cả hai phe đều giàu vì ván kéo dài".

    /// <summary>Tổng net worth 5 người cùng phe, gồm cả người được theo dõi.</summary>
    public int? TeamNetWorth { get; set; }

    /// <summary>Tổng net worth 5 người phe địch.</summary>
    public int? EnemyNetWorth { get; set; }

    /// <summary>
    /// Trung vị phân vị GPM của 4 ĐỒNG ĐỘI (không tính người được theo dõi).
    ///
    /// Dùng phân vị chứ không dùng GPM thô: đồng đội chơi hero khác nhau, và 500 GPM của một
    /// support là phi thường còn của một carry là kém. Phân vị đã so mỗi người với người khác
    /// cùng hero nên bốn con số mới cộng chung được.
    /// </summary>
    public int? MatesPctGpm { get; set; }

    /// <summary>Trung vị phân vị XPM của 4 đồng đội.</summary>
    public int? MatesPctXpm { get; set; }

    // ---------- Đổi chác trong từng pha giao tranh, CHỈ có ở ván đã parse ----------
    //
    // VÌ SAO CẦN THÊM MỘT TẦNG NỮA. Tầng trước hỏi "ván tôi chết nhiều thì đồng đội có farm tốt
    // hơn không" và đo được +5 điểm phân vị. Nhưng nó vẫn là con số CẢ VÁN, nên không phân biệt
    // được một cái chết vô ích với một cái chết kéo 2-3 người địch đi xa để đồng đội dọn phần
    // còn lại.
    //
    // Pha giao tranh thì phân biệt được: cộng vàng cộng thêm của 5 người mỗi phe TRONG pha đó là
    // biết pha đó ai lời. Và tiền thưởng của Dota vốn tỉ lệ với độ giàu của nạn nhân, nên
    // "tôi chết rẻ, đổi lại đội giết được đứa giàu" tự nằm trong con số vàng.

    /// <summary>Số pha giao tranh có ta chết.</summary>
    public int? FightsDied { get; set; }

    /// <summary>Trong số đó, bao nhiêu pha đội vẫn LỜI vàng so với địch.</summary>
    public int? FightsDiedAhead { get; set; }

    /// <summary>Tổng chênh lệch vàng hai phe, cộng dồn qua những pha có ta chết.</summary>
    public int? FightSwingDied { get; set; }

    /// <summary>Số pha giao tranh ta sống sót — nhóm đối chứng.</summary>
    public int? FightsSurvived { get; set; }

    /// <summary>Tổng chênh lệch vàng cộng dồn qua những pha ta sống sót.</summary>
    public int? FightSwingSurvived { get; set; }

    /// <summary>
    /// Cộng dồn ĐỘ GIÀU CỦA TA tại thời điểm những pha có ta chết.
    ///
    /// Lấy vàng theo phút chứ không lấy net worth cuối ván: net worth cuối là con số SAU khi mọi
    /// chuyện đã xảy ra, còn thứ quyết định một cuộc đổi chác lời hay lỗ là độ giàu NGAY LÚC ĐÓ.
    /// </summary>
    public int? TradeMyGold { get; set; }

    /// <summary>Cộng dồn độ giàu của những kẻ địch chết trong CHÍNH những pha đó.</summary>
    public int? TradeFoeGold { get; set; }

    /// <summary>Số kẻ địch đã chết trong những pha đó — mẫu số cho <see cref="TradeFoeGold"/>.</summary>
    public int? TradeFoeDeaths { get; set; }

    // ---------- Giai đoạn lane, CHỈ có ở ván đã parse ----------
    //
    // VÌ SAO PHẦN NÀY ĐẶC BIỆT SẠCH. Mọi chỉ số khác trên trang đều đo lúc ván đã kết thúc, nên
    // so giữa ván thắng và ván thua luôn vướng vòng nhân quả: thắng thì chỉ số nào cũng đẹp.
    // Còn hiệu suất lane và chênh lệch vàng ở phút 10 được đo TRƯỚC KHI ván ngã ngũ — nên so
    // giữa thắng và thua ở mốc đó nói được điều thật: bạn thua từ lane, hay thắng lane rồi mất.

    /// <summary>
    /// lane_efficiency_pct của OpenDota: phần tài nguyên lane thực sự lấy được, tính theo phần trăm.
    /// </summary>
    public int? LaneEfficiency { get; set; }

    /// <summary>Chênh lệch vàng hai phe ở phút 10, theo góc nhìn PHE TA. Dương = ta dẫn.</summary>
    public int? GoldAdv10 { get; set; }

    /// <summary>Như trên, ở phút 20.</summary>
    public int? GoldAdv20 { get; set; }

    /// <summary>Như trên, ở phút 30. null khi ván kết thúc trước đó.</summary>
    public int? GoldAdv30 { get; set; }

    /// <summary>4 người cùng phe. Rỗng khi ván chưa lấy chi tiết, hoặc khi cả 4 đều ẩn danh.</summary>
    public List<TrackedMatchTeammate> Teammates { get; set; } = [];
}
