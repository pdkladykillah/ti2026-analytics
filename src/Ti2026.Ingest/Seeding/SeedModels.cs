using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ti2026.Ingest.Seeding;

/// <summary>
/// DTO khớp CHÍNH XÁC 6 file JSON hiện tại trong data/.
///
/// Dùng cho cả hai chiều: đọc file để seed, và làm response của API. Một định nghĩa duy
/// nhất cho một shape — tách đôi thì hai bên sẽ trôi khỏi nhau và trang sẽ vỡ lặng lẽ.
///
/// index.html parse trực tiếp các khoá này (dòng 254-269, 284, 295): d.teams, x.rosters,
/// x.pairs, pl.players, pl.h, pl.i, m.updatedAt, m.seed. Đổi một tên field là trang trắng.
/// </summary>
public static class SeedJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

// ---------- teams.json ----------

public class TeamsFile
{
    public string? Event { get; set; }
    public string? Source { get; set; }
    public List<TeamDto> Teams { get; set; } = [];
}

public class TeamDto
{
    public string Name { get; set; } = "";
    public string? Short { get; set; }
    public string Slug { get; set; } = "";
    public string? Region { get; set; }
    public string? Qualification { get; set; }
    public bool? SlugVerified { get; set; }
    public TeamStatsDto? Stats { get; set; }
    public string? Logo { get; set; }

    /// <summary>
    /// team_id của OpenDota do người biên tập chỉ định. Khi có giá trị, nó GHI ĐÈ kết quả
    /// của TeamResolver — phán quyết của con người thắng bộ so khớp tự động.
    ///
    /// Dùng khi OpenDota có nhiều đội trùng tên (ví dụ hai "LGD Gaming", một đang thi đấu
    /// một đã ngừng từ 2024) hoặc khi tên trên OpenDota khác hẳn tên thi đấu. Resolver cố ý
    /// từ chối đoán trong các ca đó, vì chọn sai sẽ gán toàn bộ ván của một đội cho đội khác.
    /// </summary>
    public int? OpenDotaTeamId { get; set; }
}

/// <summary>
/// Đơn vị phải giữ đúng như JSON cũ: winrate/firstBlood/f10/winWhenFb/winWhenF10 là
/// phần trăm nguyên; kills/deaths/assists/totalKills/killDiff là trung bình mỗi ván;
/// duration là phút.
/// </summary>
public class TeamStatsDto
{
    public int Maps { get; set; }
    public double Winrate { get; set; }
    public double Kills { get; set; }
    public double Deaths { get; set; }
    public double Assists { get; set; }
    public double FirstBlood { get; set; }
    public double F10 { get; set; }
    public double WinWhenFb { get; set; }
    public double WinWhenF10 { get; set; }
    public double Duration { get; set; }
    public double TotalKills { get; set; }
    public double KillDiff { get; set; }
}

// ---------- meta.json ----------

public class MetaFile
{
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? Window { get; set; }
    public string? Source { get; set; }
    public int TeamsWithData { get; set; }
    public int TeamsTotal { get; set; }

    /// <summary>index.html:267 hiện nhãn "(seed)" khi cờ này bật.</summary>
    public bool? Seed { get; set; }
}

// ---------- rosters.json ----------

public class RostersFile
{
    public string? Source { get; set; }
    public string? Note { get; set; }
    public Dictionary<string, List<RosterMemberDto>> Rosters { get; set; } = [];
}

public class RosterMemberDto
{
    public string Nick { get; set; } = "";
    public string? Real { get; set; }

    /// <summary>CORE|MID|OFFLANE|SUPPORT|FULL SUPPORT|COACH — index.html:295-296 map sẵn.</summary>
    public string? Role { get; set; }

    public string? Photo { get; set; }
}

// ---------- players.json ----------

public class PlayersFile
{
    public string? UpdatedAt { get; set; }
    public string? Note { get; set; }
    public List<PlayerDto> Players { get; set; } = [];

    /// <summary>Bảng tra cứu phụ — index.html:265 đọc pl.h.</summary>
    public Dictionary<string, JsonElement>? H { get; set; }

    /// <summary>Bảng tra cứu phụ — index.html:265 đọc pl.i.</summary>
    public Dictionary<string, JsonElement>? I { get; set; }
}

/// <summary>
/// Tên field một chữ vì players.json viết tắt để tiết kiệm dung lượng.
/// KHÔNG đổi tên — frontend đọc trực tiếp.
/// </summary>
public class PlayerDto
{
    public string N { get; set; } = "";     // nick
    public string? R { get; set; }          // tên thật
    public string? T { get; set; }          // slug đội
    public string? Tn { get; set; }         // tên đội
    public int? P { get; set; }             // vị trí 1..5
    public string? Pv { get; set; }         // mô tả vị trí
    public long? Id { get; set; }           // OpenDota account_id
    public string? C { get; set; }          // quốc tịch
    public string? Cc { get; set; }         // mã quốc gia
    public string? Ph { get; set; }         // ảnh
}
