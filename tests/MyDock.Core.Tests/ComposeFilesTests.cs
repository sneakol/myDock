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

    [Theory]
    [InlineData(ProjectAction.Start, "start")]
    [InlineData(ProjectAction.Stop, "stop")]
    [InlineData(ProjectAction.Restart, "restart")]
    public void ProjectCommand_targets_the_project_by_name(ProjectAction action, string verb) =>
        Assert.Equal(["compose", "-p", "shop", verb], ComposeFiles.ProjectCommand("shop", action));

    [Fact]
    public void ProjectCommand_remove_stops_and_deletes_containers_only() =>
        Assert.Equal(["compose", "-p", "shop", "rm", "-f", "-s"], ComposeFiles.ProjectCommand("shop", ProjectAction.Remove));

    [Fact]
    public void ProjectName_follows_compose_precedence()
    {
        var dir = Directory.CreateTempSubdirectory("My App.").FullName;
        try
        {
            var file = Path.Combine(dir, "docker-compose.yml");
            var env = Path.Combine(dir, ".env");
            var folder = new string(Path.GetFileName(dir).ToLowerInvariant().Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').ToArray());

            File.WriteAllText(file, "services:\n  web:\n    name: nested\n");
            Assert.Equal(folder, ComposeFiles.ProjectName(file, null));

            File.WriteAllText(file, "name: \"shop\"  # comment\nservices: {}\n");
            Assert.Equal("shop", ComposeFiles.ProjectName(file, null));

            File.WriteAllText(env, "A=1\nCOMPOSE_PROJECT_NAME=fromenv\n");
            Assert.Equal("fromenv", ComposeFiles.ProjectName(file, env));

            File.WriteAllText(file, "name: ${NAME}\n");
            File.WriteAllText(env, "A=1\n");
            Assert.Equal(folder, ComposeFiles.ProjectName(file, env));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ProjectName_is_null_for_a_dockerfile() =>
        Assert.Null(ComposeFiles.ProjectName("/p/Dockerfile", null));
}
