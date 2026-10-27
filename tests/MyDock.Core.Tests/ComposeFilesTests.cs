using MyDock.Core.Docker;

namespace MyDock.Core.Tests;

public class ComposeFilesTests
{
    [Theory]
    [InlineData("/p/Dockerfile", ComposeFileKind.Dockerfile)]
    [InlineData("/p/Dockerfile.dev", ComposeFileKind.Dockerfile)]
    [InlineData("/p/docker-compose.yml", ComposeFileKind.Compose)]
    [InlineData("/p/docker-compose.override.yaml", ComposeFileKind.Compose)]
    [InlineData("/p/compose.yaml", ComposeFileKind.Compose)]
    [InlineData("/p/readme.md", null)]
    [InlineData("/p/other.yml", null)]
    public void Detect_classifies_by_file_name(string path, ComposeFileKind? expected) =>
        Assert.Equal(expected, ComposeFiles.Detect(path));

    [Fact]
    public void FindEnvFiles_lists_env_files_with_plain_env_first()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            foreach (var name in new[] { ".env.dev", ".env", "docker-compose.yml", "app.env" })
                File.WriteAllText(Path.Combine(dir, name), "");

            var found = ComposeFiles.FindEnvFiles(Path.Combine(dir, "docker-compose.yml"));

            Assert.Equal([".env", ".env.dev"], found.Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Commands_for_compose_pass_the_env_file()
    {
        var cmds = ComposeFiles.Commands("/p/docker-compose.yml", "/p/.env.dev");

        Assert.Equal([["compose", "-f", "/p/docker-compose.yml", "--env-file", "/p/.env.dev", "up", "-d"]], cmds);
    }

    [Fact]
    public void Commands_for_compose_without_env_omit_the_flag() =>
        Assert.DoesNotContain("--env-file", ComposeFiles.Commands("/p/compose.yml", null)[0]);

    [Fact]
    public void Commands_for_dockerfile_build_then_run_with_a_sanitised_tag()
    {
        var cmds = ComposeFiles.Commands("/p/My App/Dockerfile", "/p/My App/.env");

        Assert.Equal(["build", "-t", "mydock/my-app", "-f", "/p/My App/Dockerfile", "/p/My App"], cmds[0]);
        Assert.Equal(["run", "-d", "--env-file", "/p/My App/.env", "mydock/my-app"], cmds[1]);
    }

    [Fact]
    public void Commands_rejects_unknown_files() =>
        Assert.Throws<ArgumentException>(() => ComposeFiles.Commands("/p/notes.txt", null));
}
