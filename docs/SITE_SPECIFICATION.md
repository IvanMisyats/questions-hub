# Questions Hub - Site Specification

## Overview

**Questions Hub** (База українських запитань "Що?Де?Коли?") is an online database of Ukrainian questions for the intellectual game "What? Where? When?" (Що?Де?Коли?).

**Purpose**: Provide a structured, searchable repository of game questions for players, editors, and game organizers in Ukraine.

**Target Audience**:
- Players and teams who want to practice and study past questions
- Game editors who create new question packages
- Tournament organizers who need access to question archives
- Anyone interested in the Ukrainian intellectual gaming community

**Language**: Ukrainian (uk-UA)

**Last Updated**: January 4, 2026

---

## Technology Stack

| Layer | Technology |
|-------|------------|
| Backend | C#, ASP.NET Core, Blazor Server |
| Frontend | HTML, CSS, Bootstrap, Blazor Components |
| Database | PostgreSQL with Entity Framework Core |
| Authentication | ASP.NET Core Identity |
| Containerization | Docker, Docker Compose |

---

## Domain Model

### Core Entities

- **Package (Пакет запитань)** - A collection of questions prepared for a tournament
  - Has owner (User who created it)
  - Has status: Draft, Published, or Archived
  - Has optional Preamble (Преамбула) - info from editors, usually contains testers list
  - Editors are computed from all tour editors (not stored directly)
- **Tour (Тур)** - A round within a package, typically prepared by a specific editor
  - Has Number for display (e.g., "1", "2")
  - Has optional Preamble (Преамбула) - info from editors, usually contains testers list
  - Has many-to-many relationship with Authors (as Editors)
- **Question (Запитання)** - A single question with answer, handouts, and metadata
  - Has OrderIndex for physical ordering within tour
  - Has Number for display (can be non-numeric, e.g., "F" for hexadecimal)
  - Has optional HostInstructions (Вказівка ведучому) for organizer guidance
  - Has many-to-many relationship with Authors
- **Author (Автор/Редактор)** - A person who creates questions or edits tours
  - Has FirstName (Ім'я) and LastName (Прізвище)
  - Unique constraint on (FirstName, LastName)
  - Can be linked to multiple Questions and Tours
- **User (Користувач)** - Application user with profile information

---

## Implemented Features

### 1. Home Page (Головна сторінка)
**Route**: `/`

Displays package cards in a responsive grid with total count. Packages ordered by play date (newest first). Clicking a card navigates to package detail page.

### 2. Package Detail Page (Сторінка пакету)
**Route**: `/package/{id}`

Displays full package with all tours and questions on a single scrollable page. Left sidebar provides quick navigation to any tour or question with smooth scrolling.

### 3. Question Card Component

Displays question with host instructions (Вказівка ведучому) if present, followed by handout materials (text, images, video, audio). Toggle button shows/hides answer section with correct answer, accepted/rejected alternatives, commentary, sources, and authors.

### 4. User Authentication

#### 4.1 Registration (Реєстрація)
**Route**: `/Account/Register`

Registration form with first name, last name, city, team, email, and password. New users automatically assigned "User" role. Auto-login after successful registration.

#### 4.2 Login (Вхід)
**Route**: `/Account/Login`

Email and password authentication with "Remember Me" option. Account lockout after 5 failed attempts.

#### 4.3 Logout (Вихід)
**Route**: `/Account/Logout` or `/api/Auth/logout`

Ends user session and redirects to home page.

#### 4.4 User Profile (Мій профіль)
**Route**: `/Account/Profile`

**Authorization**: Requires authentication

View and edit profile information. City and Team are editable; First name, Last name, and Email are read-only.

#### 4.5 Login Display Component

Shows login/register buttons for anonymous users. For authenticated users, shows user's name with dropdown menu for profile and logout.

### 5. Navigation & Layout

Responsive design with sidebar and main content area. Fixed header with site title, search bar placeholder (not functional), and login/user display. Tour navigation sidebar on package detail page with collapsible groups and smooth scroll.

### 6. User Roles

| Role | Description |
|------|-------------|
| Anonymous | View published packages |
| User | View all published packages, edit own profile (default for new users) |
| Editor | Create/edit own packages, view own draft packages |
| Admin | Manage all packages regardless of owner, full access |

### 7. Media Support

Supports images (.jpg, .jpeg, .png, .gif, .webp, .svg), videos (.mp4, .webm, .ogg), and audio (.mp3, .wav, .ogg, .m4a) with lazy loading and caching.

### 8. Database Seeding

On startup: creates roles (Admin, Editor, User), creates admin user from environment variables, seeds sample packages if database is empty.

### 9. Package Management (CRUD)

**Authorization**: Requires Editor or Admin role

#### 9.1 Package List (Мої пакети)
**Route**: `/manage/packages`

Displays list of packages owned by the current user (Editors see only their own; Admins see all). Shows package title, play date, tour count, question count, status, and owner (for Admins). Actions: create new, edit, delete with confirmation.

#### 9.2 Package Editor (Редагування пакету)
**Route**: `/manage/package/{id}`

Single-page editor for complete package management:

**Package Properties**:
- Title, description, play date, status (Draft/Published/Archived)
- Editors list displayed as read-only (computed from all tour editors)
- Auto-save on field blur

**Tours Management**:
- Collapsible accordion showing all tours
- Inline editing of tour number, editors (via AuthorSelector component)
- Add/delete tours with confirmation

**Questions Management**:
- List of questions within each tour, ordered by OrderIndex
- Add/delete questions with confirmation
- Question count auto-calculated

**Question Editor Modal**:
- Full question editing: number, text, answer, accepted/rejected answers, comment, source, authors (via AuthorSelector)
- Authors prefilled from tour editors when creating new question
- Prev/Next navigation buttons (including cross-tour navigation)
- Auto-save on field blur
- Handout text field and media upload for handout and comment attachments

**Package Status**:
- **Draft** - Only visible to owner and admins
- **Published** - Visible to all users
- **Archived** - Hidden from main list, accessible via direct link

---

## UI/UX Features

- Full Ukrainian interface with Ukrainian date formatting (uk-UA)
- Responsive mobile-friendly design using Bootstrap
- Custom error page and form validation

---

## Future Features (Planned)

### Search Functionality
UI placeholder exists, not implemented. Will include full-text search across questions, packages, and authors with filters.

### Package Reordering
Not implemented. Drag-and-drop or button-based reordering of tours within packages and questions within tours.

### Author/Editor Search
Partially implemented. Authors are now stored as separate entities with unique profiles. Author names are clickable links to `/editor/{id}` profile page. Profile page shows placeholder "Сторінка в розробці". Future: search for packages by author, author statistics, list of author's works.

### Interactive Play Mode
Not implemented. Timer-based question display, one question at a time, score tracking for practice sessions.

### Tournament Results
Not implemented. Upload and store tournament results, team scores, rankings.

### Comments & Ratings
Not implemented. User comments on questions, rating system for questions and packages, favorites.

### User Management (Admin)
Not implemented. Admin panel for user management, role promotion/demotion, account suspension.

### Email Notifications
Placeholder implementation. Will include real SMTP, password reset, registration confirmation.

---

## Project Structure

```
/QuestionsHub.Blazor/
├── Components/
│   ├── Account/           # Authentication pages (Login, Register, Profile, etc.)
│   ├── Layout/            # Layout components (MainLayout, NavMenu)
│   ├── Pages/             # Main pages (Home, PackageDetail, ManagePackages, EditorProfile, etc.)
│   ├── AddAuthorModal.razor # Modal for creating new authors
│   ├── AuthorSelector.razor # Multi-select autocomplete for authors/editors
│   ├── QuestionCard.razor # Question display component
│   └── TourNavigation.razor # Tour sidebar navigation
├── Controllers/           # API controllers
│   ├── AuthController.cs  # Authentication endpoints
│   ├── PackageManagementController.cs # Package CRUD API
│   └── Dto/               # Data transfer objects
├── Data/                  # Database context, seeding, migrations
├── Domain/                # Domain models (User, Package, Tour, Question, Author)
└── Infrastructure/        # Utilities and helpers
```

---

## API Endpoints

### Authentication API

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/Auth/login` | User login |
| POST | `/api/Auth/register` | User registration |
| POST/GET | `/api/Auth/logout` | User logout |

### Media API

**Authorization**: All endpoints require Editor or Admin role

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/media/questions/{id}/{target}` | Upload media for question handout or comment (target: 'handout' or 'comment') |
| DELETE | `/api/media/questions/{id}/{target}` | Delete media from question handout or comment |

> **Note**: Package, Tour, and Question management is done directly via Blazor Server components using DbContext, not via REST API.

---

## Quick Reference: What Works Now

| Feature | Status |
|---------|--------|
| View packages list | ✅ Working |
| View package details | ✅ Working |
| View questions with answers | ✅ Working |
| Tour navigation | ✅ Working |
| Media display (images/video/audio) | ✅ Working |
| User registration | ✅ Working |
| User login/logout | ✅ Working |
| User profile view/edit | ✅ Working |
| Role-based authorization | ✅ Working |
| Create/edit/delete packages | ✅ Working |
| Create/edit/delete tours | ✅ Working |
| Create/edit/delete questions | ✅ Working |
| Package ownership & status | ✅ Working |
| Media upload | ✅ Working |
| Authors management | ✅ Working |
| Author profile page | ⏳ Placeholder only |
| Search | ⏳ UI only, not functional |
| Admin user management | ❌ Not implemented |
| Interactive play mode | ❌ Not implemented |
| Comments/ratings | ❌ Not implemented |

---

## Version History

| Date | Version | Changes |
|------|---------|---------|
| Jan 2026 | 1.3 | Added Authors as separate entity with many-to-many relationships, AuthorSelector component, EditorProfile page placeholder |
| Jan 2026 | 1.2 | Removed unused Package Management REST API (Blazor uses DbContext directly) |
| Jan 2026 | 1.1 | Added Preamble to Package and Tour, removed Tour Title, Media upload implemented |
| Dec 2025 | 1.0 | Initial specification document |
