using System.Net.Sockets;
using Docker.DotNet.Models;
using MyDock.Core.Docker;
using MyDock.Core.Models;

namespace MyDock.Core.Tests;

public sealed class DockerServiceMappingTests
{
    [Fact]
    public void Maps_a_cgroup_v2_stats_response()
    {
        var response = new ContainerStatsResponse
        {
            Read = new DateTime(2026, 1, 1, 12, 0, 1, DateTimeKind.Utc),
            CPUStats = new CPUStats
            {
                CPUUsage = new CPUUsage { TotalUsage = 1200 },
                SystemUsage = 8800,
                OnlineCPUs = 8,
            },
            PreCPUStats = new CPUStats { CPUUsage = new CPUUsage { TotalUsage = 1000 }, SystemUsage = 8000 },
            MemoryStats = new MemoryStats
            {
                Usage = 5000,
                Limit = 20000,
                Stats = new Dictionary<string, ulong> { ["inactive_file"] = 1500, ["active_file"] = 10 },
            },
            Networks = new Dictionary<string, NetworkStats>
            {
                ["eth0"] = new() { RxBytes = 100, TxBytes = 10 },
                ["eth1"] = new() { RxBytes = 200, TxBytes = 20 },
            },
            BlkioStats = new BlkioStats
            {
                IoServiceBytesRecursive =
                [
                    new BlkioStatEntry { Major = 8, Op = "read", Value = 4096 },
                    new BlkioStatEntry { Major = 8, Op = "write", Value = 8192 },
                    new BlkioStatEntry { Major = 9, Op = "Read", Value = 4 },
                    new BlkioStatEntry { Major = 9, Op = "Write", Value = 8 },
                ],
            },
            PidsStats = new PidsStats { Current = 12 },
        };

        var sample = DockerService.ToSample(response);

        Assert.Equal(new DateTimeOffset(2026, 1, 1, 12, 0, 1, TimeSpan.Zero), sample.Read);
        Assert.Equal(1200ul, sample.CpuTotal);
        Assert.Equal(1000ul, sample.PreCpuTotal);
        Assert.Equal(8800ul, sample.SystemCpu);
        Assert.Equal(8000ul, sample.PreSystemCpu);
        Assert.Equal(8u, sample.OnlineCpus);
        Assert.Equal(5000ul, sample.MemoryUsage);
        Assert.Equal(1500ul, sample.MemoryInactiveFile);
        Assert.Equal(20000ul, sample.MemoryLimit);
        Assert.Equal(300ul, sample.NetRxBytes);
        Assert.Equal(30ul, sample.NetTxBytes);
        Assert.Equal(4100ul, sample.BlockReadBytes);
        Assert.Equal(8200ul, sample.BlockWriteBytes);
        Assert.Equal(12ul, sample.Pids);
    }

    [Fact]
    public void Maps_a_sparse_stats_response_without_throwing()
    {
        var sample = DockerService.ToSample(new ContainerStatsResponse());

        Assert.Equal(1u, sample.OnlineCpus);
        Assert.Equal(0ul, sample.MemoryUsage);
        Assert.Equal(0ul, sample.NetRxBytes);
        Assert.Equal(0ul, sample.BlockReadBytes);
    }

    [Fact]
    public void Falls_back_to_cgroup_v1_fields()
    {
        var sample = DockerService.ToSample(new ContainerStatsResponse
        {
            CPUStats = new CPUStats { CPUUsage = new CPUUsage { PercpuUsage = [1, 2, 3, 4] } },
            MemoryStats = new MemoryStats { Stats = new Dictionary<string, ulong> { ["total_inactive_file"] = 77 } },
        });

        Assert.Equal(4u, sample.OnlineCpus);
        Assert.Equal(77ul, sample.MemoryInactiveFile);
    }

    [Fact]
    public void Maps_a_list_entry()
    {
        var info = DockerService.ToInfo(new ContainerListResponse
        {
            ID = "0123456789abcdef",
            Names = ["/shop-db-1"],
            Image = "postgres:17",
            ImageID = "sha256:abc",
            State = "running",
            Status = "Up 2 hours (healthy)",
            Health = new HealthSummary { Status = "healthy" },
            Created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Labels = new Dictionary<string, string> { ["com.docker.compose.project"] = "shop" },
        });

        Assert.Equal(
            new ContainerInfo(
                "0123456789abcdef", "shop-db-1", "postgres:17", "sha256:abc", ContainerState.Running, HealthState.Healthy,
                "Up 2 hours (healthy)", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "shop"),
            info);
        Assert.True(info.IsActive);
    }

    [Fact]
    public void Maps_ports_and_user_defined_networks()
    {
        var info = DockerService.ToInfo(new ContainerListResponse
        {
            ID = "x",
            State = "running",
            Ports =
            [
                new() { IP = "0.0.0.0", PrivatePort = 80, PublicPort = 8080, Type = "tcp" },
                new() { IP = "::", PrivatePort = 80, PublicPort = 8080, Type = "tcp" },
                new() { PrivatePort = 5432, Type = "tcp" },
            ],
            NetworkSettings = new()
            {
                Networks = new Dictionary<string, EndpointSettings> { ["bridge"] = new(), ["shop_default"] = new() },
            },
        });

        Assert.Equal("8080→80/tcp, 5432/tcp", info.Ports);
        Assert.Equal("shop_default", info.Networks);
    }

    [Fact]
    public void Maps_a_list_entry_with_missing_fields()
    {
        var info = DockerService.ToInfo(new ContainerListResponse { ID = "0123456789abcdef", State = "exited" });

        Assert.Equal("0123456789ab", info.Name);
        Assert.Equal(ContainerState.Exited, info.State);
        Assert.Equal(HealthState.None, info.Health);
        Assert.Null(info.ComposeProject);
        Assert.Null(info.Ports);
        Assert.Null(info.Networks);
        Assert.False(info.IsActive);
    }

    [Theory]
    [InlineData("created", ContainerState.Created)]
    [InlineData("restarting", ContainerState.Restarting)]
    [InlineData("paused", ContainerState.Paused)]
    [InlineData("dead", ContainerState.Dead)]
    [InlineData("something-new", ContainerState.Unknown)]
    public void Parses_states(string state, ContainerState expected) =>
        Assert.Equal(expected, DockerService.ToInfo(new ContainerListResponse { ID = "x", State = state }).State);

    [Fact]
    public void Classifies_socket_permission_errors()
    {
        var denied = new HttpRequestException("x", new SocketException((int)SocketError.AccessDenied));
        var refused = new HttpRequestException("x", new SocketException((int)SocketError.ConnectionRefused));

        Assert.Equal(ConnectionState.PermissionDenied, DockerErrors.Classify(denied));
        Assert.Equal(ConnectionState.DaemonUnavailable, DockerErrors.Classify(refused));
        Assert.Equal(ConnectionState.DaemonUnavailable, DockerErrors.Classify(new IOException("eof")));
    }
}
