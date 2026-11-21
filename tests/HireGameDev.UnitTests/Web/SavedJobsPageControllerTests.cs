using System.Reflection;
using System.Security.Claims;
using HireGameDev.Application.Abstractions.Jobs;
using HireGameDev.Application.Common;
using HireGameDev.Application.Features.Jobs;
using HireGameDev.Web.Authorization;
using HireGameDev.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Xunit;

namespace HireGameDev.UnitTests.Web;

public sealed class SavedJobsPageControllerTests
{
    [Fact]
    public async Task Save_UsesSessionActor_AndOnlyRedirectsToValidatedJobPath()
    {
        var fake = new FakeService();
        var id = Guid.NewGuid(); var job = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) }, "test"));
        var controller = new SavedJobsPageController(fake) { ControllerContext = new ControllerContext { HttpContext = context }, TempData = new TempDataDictionary(context, new EmptyTempData()) };
        var redirect = Assert.IsType<LocalRedirectResult>(await controller.Save(job, "good-job", default));
        Assert.Equal("/jobs/good-job", redirect.Url); Assert.Equal(id, fake.Actor); Assert.Equal(job, fake.Job);
        Assert.IsType<RedirectToActionResult>(await controller.Save(job, "//evil.example", default));
        Assert.Equal("private, no-store", context.Response.Headers["Cache-Control"].ToString());
        Assert.Equal(AuthorizationPolicies.CandidateOnly, typeof(SavedJobsPageController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.NotNull(typeof(SavedJobsPageController).GetMethod(nameof(SavedJobsPageController.Save))!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(typeof(SavedJobsPageController).GetMethod(nameof(SavedJobsPageController.Remove))!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
    private sealed class EmptyTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class FakeService : ISavedJobService
    {
        public Guid Actor { get; private set; }
        public Guid Job { get; private set; }
        public Task<Result<bool>> IsSavedAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success(false));
        public Task<Result> SaveAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken = default) { Actor = actorUserId; Job = jobId; return Task.FromResult(Result.Success()); }
        public Task<Result> RemoveAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result<SavedJobPage>> ListAsync(Guid actorUserId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success(new SavedJobPage(Array.Empty<SavedJobEntry>(), 0, 1, pageSize, 1)));
    }
}
