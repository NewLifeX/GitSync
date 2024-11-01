using GitSync;

namespace TestProject1;

public class GitRepoTests
{
    GitRepo _gitRepo;
    public GitRepoTests()
    {
        _gitRepo = new GitRepo { Name = "Core", Path = "D:\\X\\NewLife.Map" };
    }

    [Fact]
    public void GetBranchs()
    {
        var rs = _gitRepo.GetBranchs();
        Assert.NotEmpty(rs);
    }

    [Fact]
    public void GetRemotes()
    {
        var rs = _gitRepo.GetRemotes();
        Assert.NotEmpty(rs);
    }

    [Fact]
    public void GetChanges()
    {
        var rs = _gitRepo.GetChanges();
        Assert.NotEmpty(rs);
    }
}