# syntax=docker/dockerfile:1

# ============================================================================
# Build stage
# ----------------------------------------------------------------------------
# Copy .csproj + Directory.Build.props TRƯỚC rồi restore, sau đó mới copy source.
# Docker cache lớp restore, nên sửa code không phải tải lại toàn bộ NuGet — quan
# trọng với VPS 2 vCPU / 1.9 GB không swap.
# ============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/Ti2026.Data/Ti2026.Data.csproj      src/Ti2026.Data/
COPY src/Ti2026.Ingest/Ti2026.Ingest.csproj  src/Ti2026.Ingest/
COPY src/Ti2026.Web/Ti2026.Web.csproj        src/Ti2026.Web/
RUN dotnet restore src/Ti2026.Web/Ti2026.Web.csproj

COPY src/ src/
RUN dotnet publish src/Ti2026.Web/Ti2026.Web.csproj \
      -c Release -o /app/publish \
      --no-restore \
      /p:UseAppHost=false

# ============================================================================
# Runtime stage
# ============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app/publish .

# data/ là NGUỒN SEED dữ liệu biên tập — EditorialSeeder đọc từ đây lúc startup.
# Thiếu dòng này thì container lên với DB rỗng và trang trắng.
COPY data ./data

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080

# KHÔNG khai HEALTHCHECK ở đây: image aspnet:10.0 là Debian slim, không có sẵn curl
# hay wget, nên mọi lệnh kiểm tra HTTP thông thường sẽ luôn thất bại và container bị
# đánh dấu "unhealthy" dù app hoàn toàn bình thường. Một healthcheck báo sai còn tệ
# hơn không có. Kiểm tra sức khoẻ từ ngoài bằng: GET <host>/ti2026/api/health
ENTRYPOINT ["dotnet", "Ti2026.Web.dll"]
