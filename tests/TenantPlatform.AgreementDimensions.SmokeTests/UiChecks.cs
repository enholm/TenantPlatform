using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using TenantPlatform.Core.Agreements;
using TenantPlatform.Core.Dimensions;
using TenantPlatform.Web;
using TenantPlatform.Web.Components.Pages.Agreements;
using TenantPlatform.Web.Components.Pages.Dimensions;
using TenantPlatform.Web.Services.Agreements;
using TenantPlatform.Web.Services.Dimensions;

static class UiChecks
{
    public static async Task Run()
    {
        await Choices();
        await Administration();
        await OrdinaryForms();
        await Analysis(false);
        await Analysis(true);
        await Reader();
    }
    static IAgreementService Forward(AgreementService service, Func<System.Reflection.MethodInfo, object?[], object?>? intercept = null) =>
        UiProxy.Make<IAgreementService>((m, a) => intercept is null ? UiProxy.Call(m, service, a) : intercept(m, a));
    static async Task Choices()
    {
        var f = new Fixture(); await f.Seed(); await f.Rule(f.Single); await f.Rule(f.Multi);
        await f.Mutate(async db =>
        {
            foreach (var translation in await db.DimensionTranslations.Where(t => t.DimensionId == f.Single).ToListAsync())
                translation.Description = "Description " + translation.LanguageCode;
        });
        var id = await f.Create([Fixture.Patch(f.Single, f.A), Fixture.Patch(f.Multi, f.M1, f.M2)]);
        var details = await f.Service().GetAsync(f.Account, id);
        var catalogJson = Fixture.Json(details.Dimensions);
        var state = new AgreementDimensionEditState(details.Dimensions, details.Revision);
        Check.That(state.Changes() is null, "UI state omits unchanged dimensions");
        state.Toggle(f.Multi, f.M1, false); state.Toggle(f.Multi, f.M1, true);
        Check.That(state.Changes() is null, "UI state compares sets without order and omits restored original");
        state.SetSingle(f.Single, f.B);
        Check.That(state.Changes() is [{ DimensionId: var changed, ValueIds: var values }] && changed == f.Single && values!.SequenceEqual([f.B]), "UI state sends only the changed explicit set");
        state.SetSingle(f.Single, null);
        Check.That(state.Changes()!.Single().ValueIds!.Count == 0, "UI state sends [] for explicit clearing");
        state.SetSingle(f.Single, f.A);
        Check.That(state.Changes() is null && Fixture.Json(details.Dimensions) == catalogJson, "UI edits never mutate original catalog");
        var request = f.Request(); request.Revision = Guid.NewGuid();
        await Check.Invalid(() => { state.ApplyTo(request); return Task.CompletedTask; }, "AgreementConcurrencyConflict", "UI rejects a mismatched model/catalog revision");
        await using var ui = new UiHarness(f, Forward(f.Service()));
        await ui.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            var mounted = await ui.Mount<AgreementDimensionChoices>(("State", state), ("Existing", true));
            Check.That(mounted.Html.Contains("<select") && mounted.Html.Contains("type=\"checkbox\"") && mounted.Html.Contains("Root / Leaf A") && mounted.Html.Contains("<label"), "picker renders labelled single/multiple controls with hierarchical paths");
            await mounted.Act("SingleChanged", f.Single, new ChangeEventArgs { Value = f.Root.ToString() });
            Check.That(state.IsSelected(f.Single, f.A) && !mounted.Html.Contains($"value=\"{f.Root}\""), "LeafOnly metadata excludes unselectable parent and handler rejects it");
            await mounted.Act("Toggle", f.Multi, f.MRoot, true);
            Check.That(state.IsSelected(f.Multi, f.MRoot), "multiple picker permits a selectable parent when LeafOnly is false");
            await mounted.Act("SingleChanged", f.Single, new ChangeEventArgs { Value = f.B.ToString() });
            Check.That(state.IsSelected(f.Single, f.B) && !state.IsSelected(f.Single, f.A), "single handler replaces the explicit set");
            await mounted.Act("SingleChanged", f.Single, new ChangeEventArgs { Value = "" });
            Check.That(state.Changes()!.Single(p => p.DimensionId == f.Single).ValueIds!.Count == 0, "single picker blank action explicitly clears the set");
            var narrowed = new AgreementDimensionEditState(details.Dimensions.Select(d => d with { AllowMultiple = false }), details.Revision);
            var narrowedUi = await ui.Mount<AgreementDimensionChoices>(("State", narrowed), ("Existing", true));
            Check.That(narrowed.Selected(f.Multi).Count == 2 && narrowed.Changes() is null && narrowedUi.Html.Contains("Flere tidligere valg"), "changing global cardinality never silently truncates existing multiple assignments during rendering");
        });
        await f.Mutate(async db => (await db.DimensionValues.SingleAsync(v => v.Id == f.A)).IsActive = false);
        await f.Rule(f.Multi, false);
        details = await f.Service().GetAsync(f.Account, id);
        var historic = new AgreementDimensionEditState(details.Dimensions, details.Revision);
        await ui.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            var mounted = await ui.Mount<AgreementDimensionChoices>(("State", historic), ("Existing", true));
            Check.That(mounted.Html.Contains("Historisk") && historic.IsSelected(f.Single, f.A) && historic.IsSelected(f.Multi, f.M1), "inactive value and unavailable dimension retain historical selections");
            await mounted.Act("Toggle", f.Single, f.A, false);
            await mounted.Act("SingleChanged", f.Single, new ChangeEventArgs { Value = f.A.ToString() });
            await mounted.Act("Toggle", f.Multi, f.M1, false); await mounted.Act("Toggle", f.Multi, f.M1, true);
            Check.That(!historic.IsSelected(f.Single, f.A) && !historic.IsSelected(f.Multi, f.M1) && historic.IsSelected(f.Multi, f.M2), "removed historical single/multiple values cannot be added again and other history remains");
            Check.That(historic.Changes()!.Single(p => p.DimensionId == f.Single).ValueIds!.Count == 0, "historical single remains explicitly removable");
        });
        var before = Fixture.Json(historic.Changes()!);
        foreach (var (lang, expected) in new[] { ("nb-NO", "Norsk SINGLE"), ("en-GB", "English SINGLE"), ("sv-SE", "Svensk SINGLE") })
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(lang);
            var local = await f.Service().GetAsync(f.Account, id);
            await ui.Renderer.Dispatcher.InvokeAsync(async () =>
            {
                var mounted = await ui.Mount<AgreementDimensionChoices>(("State", new AgreementDimensionEditState(local.Dimensions, local.Revision)));
                Check.That(mounted.Html.Contains(expected) && mounted.Html.Contains("Description " + lang) && mounted.Html.Contains(f.A.ToString()) && mounted.Html.Contains("Root / Leaf A"), "localized picker retains stable IDs and untranslated value path: " + lang);
                var l = ui.Provider.GetRequiredService<IStringLocalizer<TenantPlatformResources>>();
                Check.That(!l["AgreementDimensionsHistorical"].ResourceNotFound && !l["DimensionAgreementRules"].ResourceNotFound && !l["AgreementDimensionRecordedNames"].ResourceNotFound, "new picker/admin/history resources exist: " + lang);
            });
        }
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("nb-NO");
        Check.That(Fixture.Json(historic.Changes()!) == before, "rendering and culture changes preserve local edits");
    }
    static async Task Administration()
    {
        var f = new Fixture(); await f.Seed(); await f.Rule(f.Single);
        var service = f.Service(); var reads = 0; var saves = 0; var fail = false;
        AgreementDimensionRule? submitted = null;
        var proxy = Forward(service, (m, a) =>
        {
            if (m.Name == "GetAgreementDimensionRulesAsync") reads++;
            if (m.Name == "SaveAgreementDimensionRuleAsync")
            {
                saves++; submitted = (AgreementDimensionRule)a[1]!;
                return fail ? Task.FromException(new AgreementValidationException("AgreementConcurrencyConflict")) : f.RuleInput(submitted);
            }
            return UiProxy.Call(m, service, a);
        });
        await using var ui = new UiHarness(f, proxy);
        await ui.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = await ui.Mount<DimensionsPage>();
            var register = page.Field<DimensionRegister>("register");
            await page.Act("SelectDimension", register.Dimensions.Single(d => d.Id == f.Single));
            var tabField = typeof(DimensionsPage).GetField("activeTab", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            page.SetField("activeTab", Enum.Parse(tabField.FieldType, "Agreements")); page.Refresh();
            Check.That(page.Html.Contains("Bruk i Avtaler") && page.Html.Contains("Bruk i Leasing") && page.Html.Contains("dimension-agreements-required") && !page.Html.Contains("dimension-leasing-required"), "administration has separate Agreements/Leasing tabs");
            var rule = page.Field<AgreementDimensionRule>("agreementRule");
            var leasingBefore = Fixture.Json(page.Field<TenantPlatform.Core.Leasing.LeasingDimensionRule>("rule"));
            var editor = await ui.Mount<AgreementDimensionRulesSection>(("Rule", rule), ("OnSave", EventCallback.Factory.Create(new object(), () => page.Act("SaveAgreementRule"))));
            rule.IsEnabled = false; rule.IsRequired = true; await editor.Act("RequiredChanged");
            Check.That(rule.IsEnabled && rule.IsRequired, "required toggle also enables availability");
            rule.IsEnabled = false; await editor.Act("EnabledChanged");
            Check.That(!rule.IsRequired, "disabling availability also clears required");
            rule.IsRequired = true; await editor.Act("RequiredChanged");
            await editor.Act("SaveAsync");
            Check.That(saves == 1 && submitted!.IsEnabled && submitted.IsRequired && reads == 2, "admin action calls Agreements save and reloads only on success");
            Check.That(Fixture.Json(page.Field<TenantPlatform.Core.Leasing.LeasingDimensionRule>("rule")) == leasingBefore, "Agreements rule editing does not mutate Leasing form state");
            await using (var db = f.Open())
                Check.That((await db.LeasingDimensionRules.SingleAsync()).IsRequired && (await db.AgreementDimensionRules.SingleAsync()).IsRequired, "Agreements save preserves the independent stored Leasing rule");
            rule = page.Field<AgreementDimensionRule>("agreementRule"); var revision = rule.Revision;
            rule.IsRequired = false; fail = true; await page.Act("SaveAgreementRule");
            Check.That(page.Field<string>("error") == "AgreementConcurrencyConflict" && !page.Field<AgreementDimensionRule>("agreementRule").IsRequired && rule.Revision == revision && reads == 2 && saves == 2, "admin conflict retains input/revision without reload or automatic retry");
            var current = await ui.Mount<DimensionHistoryView>(("Json", "{\"Module\":\"Agreements\",\"IsEnabledInAgreements\":true,\"IsRequiredAtRegistration\":false}"));
            Check.That(current.Html.Contains("Bruk i Avtaler") && current.Html.Contains("Tilgjengelig") && current.Html.Contains("Obligatorisk ved registrering"), "Agreements rule history renders meaningful localized labels");
            var old = await ui.Mount<DimensionHistoryView>(("Json", "{\"Name\":\"Old dimension\",\"IsEnabled\":true,\"AllowAllocation\":true}"));
            Check.That(old.Html.Contains("Old dimension") && old.Html.Contains("Leasing") && !old.Html.Contains("Bruk i Avtaler"), "legacy Dimensions/Leasing history retains its labels");
        });
    }
    static async Task OrdinaryForms()
    {
        var f = new Fixture(); await f.Seed(); await f.Rule(f.Single);
        var id = await f.Create([Fixture.Patch(f.Single, f.A)]);
        await f.Rule(f.Multi, true, true); // Older agreement now lacks a required dimension.
        var service = f.Service(); var loads = 0; var writes = 0; var fail = true; SaveAgreementRequest? captured = null;
        var proxy = Forward(service, (m, a) =>
        {
            if (m.Name is "GetAsync" or "GetOptionsAsync") loads++;
            if (m.Name is "CreateAsync" or "UpdateAsync")
            {
                writes++; captured = (SaveAgreementRequest)a[m.Name == "CreateAsync" ? 1 : 2]!;
                if (fail && m.Name == "UpdateAsync") return Task.FromException(new AgreementValidationException("AgreementConcurrencyConflict"));
                return m.Name == "CreateAsync" ? f.CreateRequest(captured) : f.UpdateRequest((Guid)a[1]!, captured);
            }
            return UiProxy.Call(m, service, a);
        });
        await using var ui = new UiHarness(f, proxy);
        await ui.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            var edit = await ui.Mount<AgreementEdit>(("Id", (Guid?)id));
            var model = edit.Field<SaveAgreementRequest>("model"); var state = edit.Field<AgreementDimensionEditState>("dimensionState"); var revision = model.Revision;
            Check.That(edit.Html.Contains("Obligatorisk verdi mangler") && !edit.Html.Contains("id=\"agreement-geographic-area\"") && !edit.Html.Contains("id=\"agreement-organization-element\"") && edit.Html.Contains("id=\"building\"") && edit.Html.Contains("id=\"counterparty\""), "ordinary edit replaces legacy classifiers and retains counterparty/building fields and missing-required guidance");
            model.Title = "Local title"; model.Terms = "Local terms"; state.SetSingle(f.Single, f.B);
            await edit.Act("SaveAsync");
            Check.That(captured!.DimensionChanges!.Single().ValueIds!.SequenceEqual([f.B]) && edit.Html.Contains("Local title") && edit.Html.Contains("Local terms") && state.IsSelected(f.Single, f.B), "failed update sends changed set and preserves dimensions and other fields");
            Check.That(model.Revision == revision && state.Revision == revision && loads == 2 && writes == 1 && ui.Navigation.Destination is null && edit.Field<string>("error") == "AgreementConcurrencyConflict", "update conflict retains original revision with no reload, retry or success navigation");
            await edit.Act("OnParametersSetAsync");
            Check.That(loads == 2 && edit.Field<SaveAgreementRequest>("model").Title == "Local title", "same-resource parameter rerender does not overwrite local edits");
            state.SetSingle(f.Single, f.A); fail = false;
            await edit.Act("SaveAsync");
            Check.That(captured.DimensionChanges is null && ui.Navigation.Destination == $"/agreements/{id}", "restored set is omitted and older missing-required agreement can save unrelated edit");
            var persisted = await service.GetAsync(f.Account, id);
            Check.That(persisted.Title == "Local title" && persisted.Terms == "Local terms" && persisted.CounterpartyOrganizationId == f.Party && persisted.OwnerUserId == f.Owner, "ordinary UI round-trip preserves title, terms, owner and actual counterparty");
            var create = await ui.Mount<AgreementEdit>();
            var newModel = create.Field<SaveAgreementRequest>("model"); var newState = create.Field<AgreementDimensionEditState>("dimensionState");
            newModel.Title = "New title"; newModel.CounterpartyOrganizationId = f.Party; newModel.Form = AgreementForm.Ongoing; newModel.NoticeCount = 3; newModel.NoticeUnit = AgreementNoticeUnit.Months;
            newState.SetSingle(f.Single, f.A); fail = true; var beforeLoads = loads;
            await create.Act("SaveAsync");
            Check.That(create.Field<string>("error") == "AgreementDimensionRequired" && create.Html.Contains("New title") && newState.IsSelected(f.Single, f.A) && loads == beforeLoads, "creation validation preserves partial dimensions and all local fields without reload");
            newState.Toggle(f.Multi, f.MRoot, true); fail = false; await create.Act("SaveAsync");
            Check.That(captured!.DimensionChanges!.Count == 2 && ui.Navigation.Destination!.StartsWith("/agreements/") && create.Field<string?>("error") is null, "ordinary creation submits same generic patches including required selectable parent");
        });
    }
    static async Task Analysis(bool existing)
    {
        var f = new Fixture(); await f.Seed(); await f.Rule(f.Single, true, true);
        Guid? id = existing ? await f.Create([Fixture.Patch(f.Single, f.A)]) : null;
        var service = f.Service(); var loads = 0; var writes = 0; var fail = true; var staleDraft = false; ApproveAnalysisRequest? captured = null;
        var proxy = Forward(service, (m, a) => { if (m.Name is "GetOptionsAsync" or "GetAgreementDimensionOptionsAsync") loads++; return UiProxy.Call(m, service, a); });
        var revision = id.HasValue ? (await service.GetAsync(f.Account, id.Value)).Revision : (Guid?)null;
        var draftId = Guid.NewGuid(); var fileId = Guid.NewGuid();
        var result = new ContractAnalysisResult(new("Original party", "123", "Address", false, ""), [], [new(fileId, true, "Processed")], [], []);
        AnalysisDraftDto Draft(bool ready) => new(draftId, [new(fileId, "contract.pdf", 15, AgreementDocumentCategory.Contract)], ready ? result : null, staleDraft ? Guid.NewGuid() : revision);
        var analysis = UiProxy.Make<IAgreementAnalysisService>((m, a) => m.Name switch
        {
            "get_MaxAnalysisFileSizeBytes" => 1024L * 1024,
            "StartAnalysisDraftAsync" => Task.FromResult(Draft(false)),
            "AnalyzeAsync" => Task.FromResult(Draft(true)),
            "FindAnalysisOrganizationsAsync" => Task.FromResult(new List<AnalysisOrganizationDto> { new(f.Party, "Business counterparty", "123") }),
            "ApproveAnalysisAsync" => Approve((ApproveAnalysisRequest)a[2]!),
            "DiscardAnalysisAsync" => Task.CompletedTask,
            _ => throw new NotSupportedException(m.Name)
        });
        Task<Guid> Approve(ApproveAnalysisRequest request)
        {
            writes++; captured = request;
            return fail ? Task.FromException<Guid>(new AgreementValidationException(existing ? "AgreementConcurrencyConflict" : "AgreementDimensionRequired")) : Task.FromResult(id ?? Guid.NewGuid());
        }
        await using var ui = new UiHarness(f, proxy, analysis: analysis);
        await ui.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            var dialog = await ui.Mount<AgreementAnalysisDialog>(("AgreementId", id));
            await dialog.Act("OpenAsync"); await dialog.Act("AnalyzeAsync");
            var approval = dialog.Field<ApproveAnalysisRequest>("approval"); var state = dialog.Field<AgreementDimensionEditState>("dimensionState");
            Check.That(dialog.Html.Contains("agreement-fields-dimensions") && dialog.Html.Contains("contract.pdf") && dialog.Html.Contains("analysis-organization"), "analysis renders generic dimensions and existing document/counterparty flow: existing=" + existing);
            state.SetSingle(f.Single, f.B); approval.CounterpartyName = "Local counterparty"; approval.Address = "Local address"; approval.CounterpartyConfirmed = true; approval.Agreement.Title = "Local analysed title";
            var expectedRevision = approval.Agreement.Revision; var expectedLoads = loads;
            await dialog.Act("ApproveAsync");
            Check.That(captured!.Agreement.DimensionChanges!.Single().ValueIds!.SequenceEqual([f.B]) && dialog.Field<bool>("open") && approval.CounterpartyName == "Local counterparty" && approval.Address == "Local address" && approval.Agreement.Title == "Local analysed title" && state.IsSelected(f.Single, f.B) && approval.CounterpartyConfirmed, "analysis failure preserves dimension edits, document draft, confirmation and other input: existing=" + existing);
            Check.That(approval.Agreement.Revision == expectedRevision && loads == expectedLoads && writes == 1 && ui.Navigation.Destination is null, "analysis failure never reloads, retries or advances revision: existing=" + existing);
            await dialog.Act("MatchAsync");
            Check.That(ReferenceEquals(state, dialog.Field<AgreementDimensionEditState>("dimensionState")) && state.IsSelected(f.Single, f.B), "counterparty matching does not replace local dimension state: existing=" + existing);
            approval.CounterpartyConfirmed = true;
            if (existing) state.SetSingle(f.Single, f.A);
            fail = false; await dialog.Act("ApproveAsync");
            Check.That(!dialog.Field<bool>("open") && (existing ? captured!.Agreement.DimensionChanges is null : captured!.Agreement.DimensionChanges!.Count == 1), "analysis success submits changed or restored sets correctly: existing=" + existing);
            if (existing)
            {
                staleDraft = true; var writesBefore = writes;
                var stale = await ui.Mount<AgreementAnalysisDialog>(("AgreementId", id)); await stale.Act("OpenAsync");
                Check.That(stale.Field<string>("error") == "AgreementConcurrencyConflict" && stale.Field<AgreementDimensionEditState?>("dimensionState") is null, "analysis refuses catalog newer than draft revision before editing");
                await stale.Act("ApproveAsync");
                Check.That(writes == writesBefore, "incoherent analysis catalog cannot reach approval service");
            }
        });
    }
    static async Task Reader()
    {
        var f = new Fixture(); await f.Seed(); await f.Rule(f.Single);
        var id = await f.Create([Fixture.Patch(f.Single, f.A)]); await f.Grants(id);
        await f.Mutate(async db => (await db.DimensionValues.SingleAsync(v => v.Id == f.A)).IsActive = false);
        var reader = f.Service(f.Reader); var editReads = 0; var reads = 0;
        var proxy = Forward(reader, (m, a) =>
        {
            if (m.Name == "GetAsync") reads++;
            if (m.Name is "GetOptionsAsync" or "GetAgreementDimensionRulesAsync") { editReads++; throw new Exception("Forbidden edit/admin read"); }
            if (m.Name == "GetLinesAsync") return Task.FromResult(new AgreementLinesDto(Guid.Empty, false, null, "NOK", [], [], []));
            return UiProxy.Call(m, reader, a);
        });
        await using var ui = new UiHarness(f, proxy, f.Reader);
        await ui.Renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = await ui.Mount<AgreementDetails>(("Id", id));
            Check.That(reads == 1 && editReads == 0 && page.Html.Contains("Root / Leaf A") && page.Html.Contains("Historisk") && !page.Html.Contains($"/agreements/{id}/edit"), "read-only grantee opens full detail with historical assignments without edit/admin reads");
            Check.That(page.Html.Contains("Business counterparty"), "detail removes legacy classifiers while retaining actual counterparty");
            var details = await reader.GetAsync(f.Account, id);
            details.DimensionHistory[0] = details.DimensionHistory[0] with { ActorName = "Recorded actor", After = [new(f.Single, "RECORDED", "Recorded dimension", [new(f.A, "OLD", "Recorded value", "Old / recorded path")])] };
            var view = await ui.Mount<AgreementDimensionsView>(("Dimensions", details.Dimensions), ("History", details.DimensionHistory));
            Check.That(view.Html.Contains("Recorded actor") && view.Html.Contains("UTC") && view.Html.Contains("Recorded dimension") && view.Html.Contains("Old / recorded path") && view.Html.Contains("navn og stier som ble lagret"), "dimension history displays actor, time, before/after and clearly marked recorded labels");
            Check.That(!view.Html.Contains("<li>Root</li>") && !view.Html.Contains("<select") && !view.Html.Contains("<button"), "read view shows explicit leaf only and contains no edit controls");
        });
    }
}
