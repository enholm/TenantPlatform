using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TenantPlatform.Core.Agreements;

static class AccessChecks
{
    public static async Task Run()
    {
        var f = new Fixture(); await f.Seed(); await f.Rule(f.Single); await f.Rule(f.Multi);
        var id = await f.Create([Fixture.Patch(f.Single, f.A)]);
        await f.Grants(id);
        var reader = f.Service(f.Reader); var editor = f.Service(f.Editor); var owner = f.Service(f.Owner);
        var read = await reader.GetAgreementDimensionOptionsAsync(f.Account, id);
        Check.That(!read.CanEdit && read.Dimensions.Single(x => x.DimensionId == f.Single).Values.Single(x => x.IsAssigned).Path == "Root / Leaf A", "read-only grantee sees assignment details and paths without edit/admin rights");
        var detail = await reader.GetAsync(f.Account, id);
        Check.That(!detail.CanEdit && detail.Dimensions.Count == 2 && detail.DimensionChanges is null && detail.DimensionHistory.Count == 1,
            "existing detail DTO exposes dimensions/history without accidentally populating write patches");
        Check.That((await reader.GetAgreementDimensionHistoryAsync(f.Account, id)).Count == 1, "read-only grantee may read classification history");
        await Check.Error<UnauthorizedAccessException>(() => reader.GetAgreementDimensionOptionsAsync(f.Account, id, true), "read-only grantee cannot load edit catalog");
        await Check.Error<UnauthorizedAccessException>(() => reader.GetOptionsAsync(f.Account, id), "existing edit-options contract retains edit permission requirement");
        await Check.Error<UnauthorizedAccessException>(() => f.Update(id, [Fixture.Patch(f.Single, f.B)], f.Reader), "update core rejects a read-only grantee");
        var editable = await editor.GetAgreementDimensionOptionsAsync(f.Account, id, true);
        Check.That(editable.CanEdit && (await editor.GetOptionsAsync(f.Account, id)).Dimensions.Count == 2, "edit grantee can use generic and existing options without register administration");
        await f.Update(id, [Fixture.Patch(f.Single, f.B)], f.Editor);
        Check.That((await f.Assignments(id)).Single().ValueId == f.B, "edit grantee can change assignments while retaining owner");
        await Check.Error<UnauthorizedAccessException>(() => f.Update(id, user: f.Editor, edit: r => r.OwnerUserId = f.Editor), "classification integration preserves owner-change restrictions");
        await Check.Error<UnauthorizedAccessException>(() => owner.GetAgreementDimensionOptionsAsync(f.Account), "ordinary agreement owner has no create-catalog permission");
        await Check.Error<UnauthorizedAccessException>(() => owner.CreateAsync(f.Account, f.Request()), "public create rejects ordinary owner before relational operations");
        await Check.Error<UnauthorizedAccessException>(() => f.Create(user: f.Owner), "create core independently checks create permission");
        foreach (var user in new[] { f.Reader, f.Editor, f.Stranger })
            await Check.Error<UnauthorizedAccessException>(() => f.Service(user).GetAgreementDimensionRulesAsync(f.Account), "reading a contract grants no configuration-administration permission");
        var hidden = await f.Create();
        await f.Mutate(async db => (await db.Agreements.SingleAsync(x => x.Id == hidden)).OwnerUserId = f.Admin);
        foreach (var service in new[] { reader, f.Service(f.Stranger) })
        {
            await Check.Error<UnauthorizedAccessException>(() => service.GetAgreementDimensionOptionsAsync(f.Account, hidden), "inaccessible agreement does not leak dimension data");
            await Check.Error<UnauthorizedAccessException>(() => service.GetAgreementDimensionHistoryAsync(f.Account, hidden), "inaccessible agreement does not leak history");
        }
        await Check.Error<UnauthorizedAccessException>(() => f.Service().GetAgreementDimensionOptionsAsync(f.ForeignAccount, id), "mismatched selected account is rejected");
        await Check.Error<UnauthorizedAccessException>(() => f.Service(selectedAccount: f.ForeignAccount).GetAgreementDimensionRulesAsync(f.ForeignAccount), "selected account without active membership cannot use cached admin claim");
        foreach (var (culture, expected) in new[] { ("nb-NO", "Norsk SINGLE"), ("en-GB", "English SINGLE"), ("sv-SE", "Svensk SINGLE") })
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var options = await owner.GetAgreementDimensionOptionsAsync(f.Account, id);
            Check.That(options.Dimensions.Single(x => x.DimensionId == f.Single).Name == expected && options.Dimensions.Single(x => x.DimensionId == f.Single).Values.Any(x => x.Id == f.B && x.Path == "Root / Leaf B"), "localized dimension labels retain IDs and untranslated paths: " + culture);
        }
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
        await f.Rule(f.Multi, false);
        var creation = await f.Service().GetAgreementDimensionOptionsAsync(f.Account);
        Check.That(creation.Dimensions.Count == 1 && creation.Dimensions.Single().DimensionId == f.Single && creation.Dimensions.Single().Values.All(x => x.Selectable && !x.IsAssigned), "create catalog includes only available active dimensions and selectable values");
        await f.Mutate(async db => (await db.Agreements.SingleAsync(x => x.Id == id)).IsArchived = true);
        Check.That(!(await reader.GetAgreementDimensionOptionsAsync(f.Account, id)).CanEdit && (await reader.GetAgreementDimensionHistoryAsync(f.Account, id)).Count > 0, "archived agreement remains readable including history");
        await Check.Invalid(() => owner.GetAgreementDimensionOptionsAsync(f.Account, id, true), "FollowupArchived", "archived agreement cannot provide editable catalog");
        await Check.Invalid(() => f.Update(id, [Fixture.Patch(f.Single, f.A)]), "FollowupArchived", "archive restriction protects classification writes");
        await f.Mutate(async db => (await db.Users.SingleAsync(x => x.Id == f.Reader)).IsActive = false);
        await Check.Error<UnauthorizedAccessException>(() => reader.GetAgreementDimensionOptionsAsync(f.Account, id), "inactive user is rechecked despite cached circuit identity");
        await f.Mutate(async db => db.UserAccounts.Remove(await db.UserAccounts.SingleAsync(x => x.AccountId == f.Account && x.UserId == f.Owner)));
        await Check.Error<UnauthorizedAccessException>(() => owner.GetAgreementDimensionHistoryAsync(f.Account, id), "removed membership is rechecked before history read");
    }
}
