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
using Xunit;

namespace HireGameDev.UnitTests.Web;

public sealed class SavedJobsControllerTests
{
    [Fact]
    public async Task Endpoints_UseAuthenticatedActorAndProtectPrivateResponses()
    {
        var fake = new FakeService();
        var userId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "test"));
        context.Request.QueryString = new QueryString("?actorUserId=" + Guid.NewGuid());
        var controller = new SavedJobsController(fake) { ControllerContext = new ControllerContext { HttpContext = context } };
        var jobId = Guid.NewGuid();
        Assert.IsType<NoContentResult>(await controller.Save(jobId, default));
        Assert.Equal(userId, fake.Actor);
        Assert.Equal(jobId, fake.JobId);
        Assert.Equal("private, no-store", context.Response.Headers["Cache-Control"].ToString());
        Assert.Equal("Cookie", context.Response.Headers["Vary"].ToString());
        Assert.IsType<OkObjectResult>(await controller.List());
        Assert.Equal(userId, fake.Actor);
        Assert.Equal(AuthorizationPolicies.CandidateOnly, typeof(SavedJobsController).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        Assert.NotNull(typeof(SavedJobsController).GetMethod(nameof(SavedJobsController.Save))!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(typeof(SavedJobsController).GetMethod(nameof(SavedJobsController.Remove))!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
    private sealed class FakeService : ISavedJobService
    {
        public Guid Actor { get; private set; }
        public Guid JobId { get; private set; }
        public Task<Result> SaveAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken = default)
        { Actor = actorUserId; JobId = jobId; return Task.FromResult(Result.Success()); }
        public Task<Result> RemoveAsync(Guid actorUserId, Guid jobId, CancellationToken cancellationToken = default)
        { Actor = actorUserId; JobId = jobId; return Task.FromResult(Result.Success()); }
        public Task<Result<SavedJobPage>> ListAsync(Guid actorUserId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
        { Actor = actorUserId; return Task.FromResult(Result.Success(new SavedJobPage(Array.Empty<SavedJobEntry>(), 0, 1, pageSize, 1))); }
    }
}
