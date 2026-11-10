# Feature: Dimensions Administration – UI Redesign

## 1. Objective

Redesign the existing Dimensions administration interface to provide a cleaner, more intuitive user experience.

The current interface combines three responsibilities on one page:

1. Creating and maintaining dimensions.
2. Configuring dimension rules.
3. Creating and maintaining dimension values.

The objective is to separate these responsibilities visually while preserving all existing functionality, business rules, authorization and persistence behavior.

This is primarily a UI/UX refactoring task.

## 2. Functional scope

### 2.1 Dimensions overview

Provide a clear overview of existing dimensions.

Users must be able to:

- View available dimensions.
- Create a new dimension.
- Edit an existing dimension.
- Access the rules associated with a dimension.
- Access and maintain its dimension values.
- Perform existing deletion or deactivation operations where supported.

Use a clean, consistent layout aligned with the existing TenantPlatform UI conventions.

### 2.2 Navigation

Prefer a master-detail design:

- Main view: Dimensions overview.
- Selecting a dimension opens its details.
- Details provide clearly separated sections for:
  - Dimension properties
  - Dimension rules
  - Dimension values

Use tabs, nested navigation or another established UI pattern already present in TenantPlatform.

Avoid introducing a new UI framework.

The final choice should be based on existing application conventions.

### 2.3 Create and edit dialogs

Prefer modal dialogs or overlays for creating and editing dimensions and dimension values.

Dialogs should:

- Have clear titles and actions.
- Provide appropriate validation feedback.
- Support Save and Cancel.
- Preserve unsaved changes until the user explicitly cancels or navigates away.
- Avoid accidental duplicate submissions.
- Show success and error feedback using existing application patterns.

### 2.4 Dimension rules

Preserve all existing rule configuration capabilities.

Rules must remain associated with the correct dimension or applicable scope.

Provide a dedicated, clearly labeled section for managing rules.

Do not simplify or remove existing rule types or validation behavior.

### 2.5 Dimension values

Provide a dedicated section for dimension values.

Users must be able to perform all currently supported operations.

Preserve existing properties, relationships, validation and authorization behavior.

## 3. Authorization

Preserve existing authorization checks and scope restrictions.

Do not move authorization enforcement into UI-only logic.

Do not introduce new permissions or change the authorization model.

Unauthorized actions must remain unavailable and must be rejected by existing backend controls.

## 4. Data and business rules

Do not modify:

- Database schema
- EF Core migrations
- Domain entities
- Business rules
- Existing persistence semantics
- Public API contracts

If a change to these areas appears necessary, stop and request approval.

## 5. User experience requirements

- Use Norwegian labels consistent with the existing application.
- Match existing styling, spacing and component conventions.
- Avoid overcrowded pages.
- Keep related actions close to their relevant data.
- Provide clear empty states.
- Support reasonable desktop and mobile layouts.
- Preserve accessibility, keyboard navigation and meaningful form labels.
- Avoid introducing unnecessary navigation complexity.

## 6. Implementation guidance

Before making changes:

1. Locate the existing Dimensions page and related components.
2. Inventory all existing functionality.
3. Identify current authorization and validation mechanisms.
4. Identify reusable UI components and patterns.
5. Propose an implementation plan that preserves existing behavior.

Prefer extracting smaller Razor components over introducing large new abstractions.

Avoid duplicating business logic across components.

Keep the changes focused on the Dimensions administration interface.

## 7. Acceptance criteria

The implementation is complete when:

- All existing dimension operations remain available.
- All existing rule configuration operations remain available.
- All existing dimension value operations remain available.
- The three responsibilities are clearly separated in the UI.
- Create and edit operations use a consistent interaction pattern.
- Existing authorization remains intact.
- Existing validation behavior is preserved.
- No database migrations are required.
- The application builds successfully.
- Relevant automated tests pass.
- New or updated tests cover important UI behavior where practical.
- No unrelated functionality is changed.

## 8. Out of scope

- New dimension types
- New business rules
- Database redesign
- Authorization redesign
- Changes to other administration modules
- New UI frameworks
- Unrelated refactoring

## 9. Completion report

Provide:

- Summary of the final UI structure.
- List of modified and added files.
- Confirmation of preserved functionality.
- Test results.
- Any behavior that could not be verified automatically.
- Known limitations and recommended manual checks.

Do not automatically commit or merge changes.
