using GitSync.Models;
using GitSync.Services;

namespace TestProject1;

public class ProjectServiceTests
{
    [Fact]
    public void UpdateWorkflow()
    {
        var repo = new Repo
        {
            Name = "NewLife.Core",
            Path = "D:\\X\\TestRepo",
        };
        var service = new ProjectService(null, null);

        service.UpdateWorkflow(repo, "D:\\X\\NewLife.Core");
    }
}
