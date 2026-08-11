using FluentAssertions;
using Ti2026.Ingest.Analytics;

namespace Ti2026.Tests;

/// <summary>
/// "Chơi lúc nào thì hay" — ba câu hỏi về hành vi, chạy trên dữ liệu đã có sẵn.
///
/// Rủi ro chính của bộ này không phải sai số mà là ĐO NHẦM THỨ KHÁC: ván đi sau trong phiên vừa
/// dễ là ván "sau khi thua" hơn, vừa là ván người ta đã mỏi hơn. Không khớp theo thứ tự thì phần
/// mỏi sẽ bị tính hết vào phần tilt.
/// </summary>
public class PlayHabitsTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Một phiên gồm <paramref name="results"/> ván nối nhau cách 10 phút.</summary>
    private static List<HabitGame> Session(
        DateTime start, IEnumerable<(bool Won, int Farm, int Deaths)> results)
    {
        var list = new List<HabitGame>();
        var t = start;
        foreach (var (won, farm, deaths) in results)
        {
            list.Add(new HabitGame(t, 2400, won, farm, deaths));
            t = t.AddSeconds(2400).AddMinutes(10);
        }
        return list;
    }

    // ---------- Phiên chơi ----------

    [Fact]
    public void Nghi_ngan_thi_cung_phien_nghi_dai_thi_phien_moi()
    {
        var g = new List<HabitGame>();
        for (var d = 0; d < 40; d++)
        {
            // Mỗi ngày một phiên 4 ván, các ngày cách nhau hẳn.
            g.AddRange(Session(T0.AddDays(d),
                Enumerable.Range(0, 4).Select(i => (i % 2 == 0, 50, 50))));
        }

        var r = PlayHabits.Read(g)!.Value;

        r.Sessions.Should().Be(40);
        r.GamesPerSession.Should().Be(4);
    }

    /// <summary>
    /// Nghỉ đúng bằng ngưỡng thì vẫn là cùng phiên; hơn ngưỡng một chút là phiên mới. Kiểm mép
    /// vì đây là chỗ một dấu &gt; hay &gt;= sẽ đổi kết quả mà không ai thấy.
    /// </summary>
    [Fact]
    public void Kiem_dung_mep_nguong_phien()
    {
        var justInside = new List<HabitGame>
        {
            new(T0, 2400, true, 50, 50),
            new(T0.AddSeconds(2400).AddMinutes(PlayHabits.SessionBreakMinutes), 2400, true, 50, 50),
        };
        var justOutside = new List<HabitGame>
        {
            new(T0, 2400, true, 50, 50),
            new(T0.AddSeconds(2400).AddMinutes(PlayHabits.SessionBreakMinutes + 1), 2400, true, 50, 50),
        };

        // Đủ ván để Read() chịu chạy: nhân bản mỗi ca lên nhiều ngày.
        List<HabitGame> Blow(List<HabitGame> pair) =>
            Enumerable.Range(0, 80).SelectMany(d =>
                pair.Select(x => x with { StartTime = x.StartTime.AddDays(d) })).ToList();

        PlayHabits.Read(Blow(justInside))!.Value.Sessions.Should().Be(80);
        PlayHabits.Read(Blow(justOutside))!.Value.Sessions.Should().Be(160);
    }

    // ---------- Tilt ----------

    /// <summary>
    /// Bài kiểm quan trọng nhất. Dựng dữ liệu mà trong đó KHÔNG có tilt thật — người chơi tệ dần
    /// theo thứ tự ván, chỉ vậy thôi. Vì ván sau thường là ván "sau khi thua", một phép so không
    /// khớp thứ tự sẽ thấy một hiệu ứng tilt to và hoàn toàn giả.
    /// </summary>
    [Fact]
    public void Met_dan_theo_thu_tu_KHONG_duoc_doc_thanh_tilt()
    {
        var g = new List<HabitGame>();
        var rnd = new Random(11);

        for (var d = 0; d < 200; d++)
        {
            var t = T0.AddDays(d);
            for (var p = 1; p <= 5; p++)
            {
                // Farm chỉ phụ thuộc THỨ TỰ, không phụ thuộc kết quả ván trước.
                var farm = Math.Clamp(80 - (p - 1) * 12 + rnd.Next(-3, 4), 0, 100);
                g.Add(new HabitGame(t, 2400, rnd.Next(2) == 0, farm, 50));
                t = t.AddSeconds(2400).AddMinutes(10);
            }
        }

        var tilt = PlayHabits.Read(g)!.Value.Tilt!.Value;

        Math.Abs(tilt.FarmGap).Should().BeLessThan(PlayHabits.MinGap,
            "không có tilt thật, nên chênh sau khi khớp thứ tự phải gần 0");
    }

    /// <summary>Nhưng tilt THẬT thì phải bắt được, nếu không bộ này chỉ là một cách im lặng.</summary>
    [Fact]
    public void Tilt_that_thi_phai_bat_duoc()
    {
        var g = new List<HabitGame>();
        var rnd = new Random(3);

        for (var d = 0; d < 200; d++)
        {
            var t = T0.AddDays(d);
            bool? prev = null;
            for (var p = 1; p <= 5; p++)
            {
                // Farm phụ thuộc KẾT QUẢ VÁN TRƯỚC, không phụ thuộc thứ tự.
                var farm = Math.Clamp((prev == false ? 45 : 70) + rnd.Next(-3, 4), 0, 100);
                var won = rnd.Next(2) == 0;
                g.Add(new HabitGame(t, 2400, won, farm, 50));
                prev = won;
                t = t.AddSeconds(2400).AddMinutes(10);
            }
        }

        var tilt = PlayHabits.Read(g)!.Value.Tilt!.Value;

        tilt.FarmGap.Should().BeLessThan(-15, "sau khi thua thì farm tụt hẳn");
        tilt.FarmP.Should().BeLessThan(0.001);
        tilt.GamesAfterWin.Should().BeGreaterThan(PlayHabits.MinGamesPerGroup);
        tilt.GamesAfterLoss.Should().BeGreaterThan(PlayHabits.MinGamesPerGroup);
    }

    [Fact]
    public void Van_dau_phien_khong_co_van_truoc_nen_khong_vao_phep_so_tilt()
    {
        // Mỗi ngày đúng MỘT ván: không ván nào có ván liền trước trong cùng phiên.
        var g = Enumerable.Range(0, 300)
            .Select(d => new HabitGame(T0.AddDays(d), 2400, d % 2 == 0, 60, 40))
            .ToList();

        PlayHabits.Read(g)!.Value.Tilt.Should().BeNull();
    }

    // ---------- Mỏi theo thứ tự ----------

    [Fact]
    public void Xep_duoc_theo_van_thu_may_trong_phien()
    {
        var g = new List<HabitGame>();
        for (var d = 0; d < 200; d++)
        {
            var t = T0.AddDays(d);
            for (var p = 1; p <= 6; p++)
            {
                g.Add(new HabitGame(t, 2400, p % 2 == 0, 90 - (p - 1) * 10, 50));
                t = t.AddSeconds(2400).AddMinutes(10);
            }
        }

        var rows = PlayHabits.Read(g)!.Value.ByPosition;

        rows.Should().HaveCount(PlayHabits.TailPosition);
        rows[0].Position.Should().Be(1);
        rows[0].Farm.Should().Be(90);
        rows[^1].IsTail.Should().BeTrue("nhóm cuối gộp mọi ván từ đó trở đi");
        rows[^1].Games.Should().Be(400, "ván thứ 5 và thứ 6 của 200 phiên");
    }

    // ---------- Giờ trong ngày ----------

    /// <summary>
    /// Giờ phải quy về GIỜ ĐỊA PHƯƠNG. Dữ liệu lưu theo UTC, mà "chơi lúc 2 giờ sáng" chỉ có
    /// nghĩa theo giờ người đó sống — lệch múi thì không chỉ sai mà còn đảo hẳn kết luận ngày/đêm.
    /// </summary>
    [Fact]
    public void Xep_theo_gio_DIA_PHUONG_chu_khong_phai_UTC()
    {
        // 20h UTC = 3h sáng hôm sau ở Việt Nam (+7), tức phải rơi vào khối 0-3h.
        var utc20 = new DateTime(2026, 3, 1, 20, 0, 0, DateTimeKind.Utc);
        var g = Enumerable.Range(0, 200)
            .Select(d => new HabitGame(utc20.AddDays(d), 2400, d % 2 == 0, 55, 45))
            .ToList();

        var rows = PlayHabits.Read(g)!.Value.ByHour;

        rows.Should().ContainSingle();
        rows[0].Hour.Should().Be(0, "20h UTC + 7 = 3h sáng, thuộc khối 0-3h");
    }

    [Fact]
    public void Khoi_gio_qua_it_van_thi_khong_hien()
    {
        var g = new List<HabitGame>();
        for (var d = 0; d < 200; d++)
            g.Add(new HabitGame(T0.AddDays(d), 2400, true, 50, 50));
        // Thêm một nhúm ván ở khối giờ khác, dưới ngưỡng.
        for (var d = 0; d < 10; d++)
            g.Add(new HabitGame(T0.AddDays(d).AddHours(8), 2400, true, 50, 50));

        PlayHabits.Read(g)!.Value.ByHour.Should().ContainSingle();
    }

    [Fact]
    public void Qua_it_van_thi_tra_null_chu_khong_no()
    {
        PlayHabits.Read([]).Should().BeNull();
        PlayHabits.Read([new HabitGame(T0, 2400, true, 50, 50)]).Should().BeNull();
    }
}
