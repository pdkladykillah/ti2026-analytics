namespace Ti2026.Data.Entities;

public class Match
{
    /// <summary>OpenDota match_id — không tự tăng.</summary>
    public long Id { get; set; }

    /// <summary>Để gom các game thành series Bo3/Bo5. API H2H tính từ đây.</summary>
    public long? SeriesId { get; set; }

    /// <summary>
    /// UTC. Dùng DateTime chứ không DateTimeOffset vì SQLite không hỗ trợ DateTimeOffset
    /// trong ORDER BY — nó lưu thành TEXT kèm offset nên so sánh chuỗi sẽ sai giữa các múi
    /// giờ. Toàn bộ pipeline sắp xếp và lọc theo cột này nên nó phải so sánh được.
    /// DbContext có converter buộc mọi DateTime đọc/ghi đều là UTC.
    /// </summary>
    public DateTime StartTime { get; set; }

    public int DurationSeconds { get; set; }
    public long? LeagueId { get; set; }
    public string? LeagueName { get; set; }

    /// <summary>Nullable: OpenDota đôi khi thiếu ánh xạ đội cho một số trận.</summary>
    public int? RadiantTeamId { get; set; }
    public int? DireTeamId { get; set; }

    public bool RadiantWin { get; set; }
    public int RadiantScore { get; set; }
    public int DireScore { get; set; }
    public int? FirstBloodTimeSeconds { get; set; }
    public bool? RadiantHadFirstBlood { get; set; }
    public bool? RadiantReachedTenFirst { get; set; }
    public string? PatchVersion { get; set; }

    /// <summary>UTC.</summary>
    public DateTime IngestedAt { get; set; }

    /// <summary>
    /// UTC, thời điểm đã nạp match detail (matches/{id}) cho ván này.
    /// null = mới chỉ có dữ liệu mức đội, chưa có first blood / assists / timeline.
    /// Ingester dùng cột này để biết còn ván nào cần nạp, và để không gọi lại ván đã có.
    /// </summary>
    public DateTime? DetailsIngestedAt { get; set; }

    /// <summary>
    /// Phiên bản của BỘ TRƯỜNG đã trích từ match detail, không phải phiên bản của dữ liệu.
    ///
    /// Vì sao cần: mỗi lần ta trích thêm trường mới từ cùng một payload (draft, mốc mua đồ,
    /// chỉ số lane…), những ván nạp trước đó vẫn còn thiếu. Không có cột này thì cách duy nhất
    /// để nạp bù là sửa SQL tay trên production — thao tác không có test, không có vết, và một
    /// lần gõ nhầm là mất dữ liệu thật.
    ///
    /// Cách dùng: tăng <see cref="Ti2026.Ingest"/> MatchDetailIngester.SchemaVersion, deploy,
    /// rồi scheduler tự nạp bù dần. Không cần ai chạm vào DB.
    /// </summary>
    public int DetailSchemaVersion { get; set; }

    /// <summary>Giây tới lần hạ Roshan ĐẦU TIÊN. null = ván không ai hạ Roshan.</summary>
    public int? FirstRoshanSeconds { get; set; }

    /// <summary>
    /// Vàng dẫn trước lớn nhất mà bên THUA từng có — OpenDota gọi là "throw".
    /// Đây là chất kể chuyện: "dẫn trước 12 nghìn vàng rồi thua" là một trận đáng nhớ,
    /// còn bảng tỷ số thì chỉ ghi 0–1.
    /// </summary>
    public int? ThrowGold { get; set; }

    /// <summary>Vàng bị dẫn lớn nhất mà bên THẮNG từng chịu — mặt kia của throw.</summary>
    public int? ComebackGold { get; set; }

    public List<MatchPlayer> Players { get; set; } = [];
}
