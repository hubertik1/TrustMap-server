using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.Api.Services;

public sealed class DemoDataSeeder(TrustMapDbContext dbContext, IPhotoStorage photoStorage)
{
    public async Task<User> CreateAsync(Guid accountId, CancellationToken cancellationToken)
    {
        Guid Id(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{accountId:N}:{key}"))[..16]);
        var tag = accountId.ToString("N")[..8];
        User Person(string key, string name, string bio)
        {
            var person = new User(key == "reviewer" ? accountId : Id(key), $"demo.{key}.{tag}#1000", name);
            person.UpdateProfile(person.Handle, name, $"Fictional demo profile. {bio}", null);
            return person;
        }

        var reviewer = Person("reviewer", "Alex · Demo", "Exploring places with friends.");
        var mia = Person("mia", "Mia · Demo", "Coffee, bakeries and weekend brunch.");
        var eli = Person("eli", "Eli · Demo", "Always looking for a great pasta dish.");
        var nora = Person("nora", "Nora · Demo", "Quiet parks and neighborhood favorites.");
        var sam = Person("sam", "Sam · Demo", "An incoming friend request to try.");
        var luca = Person("luca", "Luca · Demo", "An outgoing friend request to try.");
        var zoe = Person("zoe", "Zoe · Demo", "Find me in friend search and send a request.");
        var people = new[] { reviewer, mia, eli, nora, sam, luca, zoe };
        var storedPhotos = new List<StoredPhoto>();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            dbContext.Users.AddRange(people);
            foreach (var friend in new[] { mia, eli, nora })
                dbContext.Friendships.Add(new Friendship(Guid.CreateVersion7(), reviewer.Id, friend.Id));
            var incoming = new FriendRequest(Id("incoming"), sam.Id, reviewer.Id);
            dbContext.FriendRequests.AddRange(incoming, new FriendRequest(Id("outgoing"), reviewer.Id, luca.Id));
            dbContext.Notifications.Add(new UserNotification(Guid.CreateVersion7(), reviewer.Id, sam.Id,
                NotificationType.FriendRequestSent, friendRequestId: incoming.Id));

            var names = new[] { "Sunday Table · Demo", "Aurora Coffee · Demo", "Riverside Bakery · Demo",
                "Garden Kitchen · Demo", "Little Pasta House · Demo", "Canal Walk · Demo" };
            var authors = new[] { eli, mia, nora, reviewer, eli, reviewer };
            var dishes = new[] { "Spinach ravioli", "Flat white", "Almond croissant", "Seasonal salad", "Tagliatelle" };
            var category = new Category(Id("category"), "Demo favorites", reviewer.Id, false, "heart");
            dbContext.Categories.Add(category);
            dbContext.UserCategoryMemberships.Add(new UserCategoryMembership(reviewer.Id, category.Id,
                CategoryMembershipSource.OwnCustom));

            for (var index = 0; index < names.Length; index++)
            {
                var place = new Place(Id($"place-{index}"), names[index],
                    "Fictional demo pin near King's Cross, London", "London", "GB",
                    51.535 + index * 0.0015, -0.128 + index * 0.002,
                    null, null, reviewer.Id);
                place.SetCustomDisplayName(reviewer.Id, names[index]);
                dbContext.Places.Add(place);
                var review = new PlaceReview(Id($"review-{index}"), place.Id, authors[index].Id,
                    VisibilityLevel.Friends, index % 2 == 0 ? 5 : 4, "A favorite worth saving",
                    "Sample review for exploring TrustMap. A lovely spot to revisit with friends.");
                dbContext.PlaceReviews.Add(review);
                dbContext.PlaceCategoryAssignments.Add(new PlaceCategoryAssignment(Guid.CreateVersion7(),
                    place.Id, index < dishes.Length ? CategoryAssignmentService.RestaurantsCategoryId : category.Id,
                    reviewer.Id));

                if (authors[index] != reviewer)
                    dbContext.Notifications.Add(new UserNotification(Guid.CreateVersion7(), reviewer.Id,
                        authors[index].Id, NotificationType.PlaceReviewAdded, place.Id, review.Id));

                if (index < dishes.Length)
                {
                    var dish = new DishReview(Id($"dish-{index}"), place.Id, authors[index].Id, review.Id,
                        VisibilityLevel.Friends, dishes[index], 5,
                        "Sample dish review. Freshly prepared and worth ordering again.", 12.50m + index, "GBP");
                    dbContext.DishReviews.Add(dish);
                    if (index == 0)
                    {
                        var stored = await SaveImageAsync("food", cancellationToken);
                        storedPhotos.Add(stored);
                        dbContext.Photos.Add(new Photo(Guid.CreateVersion7(), authors[index].Id, place.Id,
                            null, dish.Id, stored.StorageProvider, stored.StorageKey, stored.PublicUrl,
                            stored.Medium?.StorageKey, stored.Medium?.PublicUrl,
                            stored.Thumbnail?.StorageKey, stored.Thumbnail?.PublicUrl,
                            "demo-ravioli.jpg", stored.ContentType, stored.SizeBytes, stored.Width, stored.Height));
                    }
                }

                if (index == 0)
                {
                    // A second author's review lets reviewers compare opinions and edit their own.
                    dbContext.PlaceReviews.Add(new PlaceReview(Id("own-sunday-review"), place.Id, reviewer.Id,
                        VisibilityLevel.Friends, 4, "Lunch with friends", "My sample review — try editing or deleting it."));
                    dbContext.DishReviews.Add(new DishReview(Id("own-dish"), place.Id, reviewer.Id,
                        Id("own-sunday-review"), VisibilityLevel.Friends, "Lemon tart", 4,
                        "My sample dish review — try editing the price or rating.", 7.50m, "GBP"));
                }
            }

            foreach (var (person, asset) in new[] { (mia, "mia"), (eli, "eli") })
            {
                var stored = await SaveImageAsync(asset, cancellationToken);
                storedPhotos.Add(stored);
                person.UpdateProfile(person.Handle, person.DisplayName, person.Bio,
                    stored.Thumbnail?.PublicUrl ?? stored.PublicUrl);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return reviewer;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            foreach (var stored in storedPhotos)
            {
                foreach (var key in new[] { stored.StorageKey, stored.Medium?.StorageKey, stored.Thumbnail?.StorageKey })
                    if (key is not null) await photoStorage.DeleteAsync(key, CancellationToken.None);
            }
            throw;
        }
    }

    private Task<StoredPhoto> SaveImageAsync(string name, CancellationToken cancellationToken)
        => SaveResourceAsync($"TrustMap.Demo.{name}.jpg", cancellationToken);

    private async Task<StoredPhoto> SaveResourceAsync(string resourceName, CancellationToken cancellationToken)
    {
        await using var stream = typeof(DemoDataSeeder).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing bundled demo image: {resourceName}");
        return await photoStorage.SaveAsync(stream, cancellationToken);
    }
}
