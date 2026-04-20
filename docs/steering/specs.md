# Feature Building Workflow: Specs

AI Assistants should follow the **Specs** workflow to transform high-level feature ideas into detailed, trackable implementation plans. This section outlines the three mandatory phases for feature development.

## Phase 1: Requirements
In this phase, define "What" needs to be built. Use **User Stories** and **Acceptance Criteria** following the **EARS** (Easy Approach to Requirements Syntax) notation.

### EARS Notation Patterns:
- **Ubiquitous**: The <system name> shall <system response>.
- **Event-driven**: WHEN <trigger> THE <system name> shall <system response>.
- **State-driven**: WHILE <precondition> THE <system name> shall <system response>.
- **Unwanted Behavior**: IF <trigger> THEN THE <system name> shall <system response>.
- **Optional Feature**: WHERE <feature is included> THE <system name> shall <system response>.

## Phase 2: Design
In this phase, define "How" the feature will be implemented technically.
- **Architecture**: How does it fit into the Clean Architecture layers (Domain, Application, Infrastructure)?
- **Contracts**: Update Proto definitions or existing service models.
- **Database**: Dapper modifications, schema changes, or migration plans.
- **Interfaces**: New interfaces or modifications to existing ones.
- **Data Flow**: Sequence diagrams or logic flows.

## Phase 3: Tasks
Break down the design into discrete, trackable implementation steps.
- Use a checklist format.
- Group tasks by layer (Domain -> Application -> Infrastructure -> Presentation).
- Include verification/testing steps (xUnit, Moq) for each major task.

---

### Example: Add Password Complexity Rule
1. **Requirements**: 
   - *User Story*: As a security administrator, I want users to set complex passwords so that accounts are better protected.
   - *EARS*: WHEN a user provides a new password, IF the password does not contain at least one special character, THEN the system shall return a validation error.
2. **Design**: Implement a custom validator using `FluentValidation` in the Application layer and wire it up in the `UserService`.
3. **Tasks**:
   - [ ] Add `PasswordValidator` in `SkeletonApi.Application/Validation`.
   - [ ] Update `UserService.cs` to use the validator during account creation.
   - [ ] Add unit tests in `SkeletonApi.Tests/Application/UserServiceTests.cs`.
