using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MiniBroker.Api.Auth;
using MiniBroker.Domain.Accounts;
using MiniBroker.Infrastructure.Persistence;

namespace MiniBroker.Api.Endpoints;

public static class AccountEndpoints
{
    private const int MaxTransactions = 500;

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts").WithTags("Accounts").RequireAuthorization();

        group.MapGet("/", List);
        group.MapPost("/", Open);
        group.MapGet("/{id:guid}", Get).WithName("GetAccount");
        group.MapPost("/{id:guid}/deposits", Deposit);
        group.MapPost("/{id:guid}/withdrawals", Withdraw);
        group.MapGet("/{id:guid}/transactions", Transactions);

        return app;
    }

    private static async Task<Ok<List<AccountResponse>>> List(HttpContext http, BrokerDbContext db, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        var accounts = await db.Accounts.AsNoTracking()
            .Include(a => a.Positions)
            .Where(a => a.OwnerUserId == userId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);
        return TypedResults.Ok(accounts.Select(AccountResponse.From).ToList());
    }

    private static async Task<CreatedAtRoute<AccountResponse>> Open(
        OpenAccountRequest request, HttpContext http, BrokerDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var account = Account.Open(http.User.GetUserId(), request.Name, clock.GetUtcNow());
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);

        return TypedResults.CreatedAtRoute(AccountResponse.From(account), "GetAccount", new { id = account.Id });
    }

    private static async Task<Results<Ok<AccountResponse>, NotFound>> Get(
        Guid id, HttpContext http, BrokerDbContext db, CancellationToken ct)
    {
        var account = await LoadOwnedAsync(db, id, http.User.GetUserId(), ct);
        return account is null ? TypedResults.NotFound() : TypedResults.Ok(AccountResponse.From(account));
    }

    private static Task<Results<Ok<AccountResponse>, NotFound>> Deposit(
        Guid id, MoneyRequest request, HttpContext http, BrokerDbContext db, TimeProvider clock, CancellationToken ct) =>
        Mutate(db, id, http.User.GetUserId(), a => a.Deposit(request.Amount, clock.GetUtcNow()), ct);

    private static Task<Results<Ok<AccountResponse>, NotFound>> Withdraw(
        Guid id, MoneyRequest request, HttpContext http, BrokerDbContext db, TimeProvider clock, CancellationToken ct) =>
        Mutate(db, id, http.User.GetUserId(), a => a.Withdraw(request.Amount, clock.GetUtcNow()), ct);

    private static async Task<Results<Ok<List<TransactionResponse>>, NotFound>> Transactions(
        Guid id, HttpContext http, BrokerDbContext db, CancellationToken ct, int take = 100)
    {
        if (!await OwnsAccountAsync(db, id, http.User.GetUserId(), ct))
            return TypedResults.NotFound();

        var items = await db.CashTransactions.AsNoTracking()
            .Where(t => t.AccountId == id)
            .OrderByDescending(t => t.CreatedAt)
            .Take(Math.Clamp(take, 1, MaxTransactions))
            .ToListAsync(ct);
        return TypedResults.Ok(items.Select(TransactionResponse.From).ToList());
    }

    private static async Task<Results<Ok<AccountResponse>, NotFound>> Mutate(
        BrokerDbContext db, Guid id, Guid userId, Action<Account> change, CancellationToken ct)
    {
        var account = await LoadOwnedAsync(db, id, userId, ct);
        if (account is null)
            return TypedResults.NotFound();

        change(account);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(AccountResponse.From(account));
    }

    /// <summary>Loads an account only if it belongs to the user — other users' accounts look like 404.</summary>
    internal static Task<Account?> LoadOwnedAsync(BrokerDbContext db, Guid id, Guid userId, CancellationToken ct) =>
        db.Accounts.Include(a => a.Positions).SingleOrDefaultAsync(a => a.Id == id && a.OwnerUserId == userId, ct);

    internal static Task<bool> OwnsAccountAsync(BrokerDbContext db, Guid id, Guid userId, CancellationToken ct) =>
        db.Accounts.AnyAsync(a => a.Id == id && a.OwnerUserId == userId, ct);
}
