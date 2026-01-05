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

**Last Updated**: January 5, 2026

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
  - Can be linked to a User account (optional one-to-one relationship)
- **User (Користувач)** - Application user with profile information
  - Can be linked to an Author entity (for Editors)

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

Shows login/register buttons for anonymous users. For authenticated users, shows user's name with dropdown menu containing:
- **Мій профіль** - User profile page
- **Мої пакети** - Package management (Editors and Admins only)
- **Редактори** - Editors list (Editors and Admins only)
- **Користувачі** - User management (Admins only)
- **Вийти** - Logout

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

### 10. Search (Пошук)
**Route**: `/search` or `/search/{query}`

Full-text search across all published questions with Ukrainian morphology support.

**Features**:
- **Ukrainian Morphology** - Finds words in different forms (відмінки, роди, числа)
- **Accent Insensitive** - Searches ignore Ukrainian accents (А́мундсен = Амундсен)
- **Typo Tolerance** - Finds results even with spelling mistakes (via trigram matching)
- **Result Highlighting** - Matched words are highlighted with `<mark>` tags in results

**Search Syntax**:
- `слово1 слово2` - Both words required (AND)
- `слово1 OR слово2` - Either word (OR)
- `"точна фраза"` - Exact phrase match
- `-слово` - Exclude word from results

**Searchable Fields** (in order of priority):
1. Question Text
2. Handout Text
3. Answer
4. Accepted/Rejected Answers
5. Comment
6. Source

### 11. Admin User Management

**Authorization**: Admin only (except Editors list which is read-only for Editors)

#### 11.1 Editors List (Редактори)
**Route**: `/admin/editors`

Displays all users with Editor role.

**Visible to Editors (read-only)**:
- Name, City, Linked Author

**Visible to Admin**:
- Name, Email, City, Linked Author
- "Понизити" button to demote editor

#### 11.2 Users List (Користувачі)
**Route**: `/admin/users`

**Authorization**: Admin only

Displays all users (except admins) with search functionality.

**Features**:
- Search by name or email
- Shows: Name, Email, City, Team, Status/Actions
- Editors shown with "Редактор" badge
- Regular users have "Зробити редактором" button
- When promoting to editor: automatically creates linked Author entity (or links to existing author with same name)

#### 11.3 Editor Profile (Профіль редактора)
**Route**: `/editor/{id}`

Displays author profile with:
- Author name
- Linked user info (name, city visible to all; email visible to admin only)
- Statistics: number of tours and questions
- Admin can link/unlink author to user account

---

## UI/UX Features

- Full Ukrainian interface with Ukrainian date formatting (uk-UA)
- Responsive mobile-friendly design using Bootstrap
- Custom error page and form validation

---

## Future Features (Planned)

### Package Reordering
Not implemented. Drag-and-drop or button-based reordering of tours within packages and questions within tours.

### Author/Editor Search
Partially implemented. Authors are now stored as separate entities with unique profiles. Author names are clickable links to `/editor/{id}` profile page. Profile page shows author name, linked user info (city visible to all, email visible to admin only), and statistics (tours and questions count). Admin can link/unlink authors to user accounts. Future: search for packages by author, list of author's works.

### Interactive Play Mode
Not implemented. Timer-based question display, one question at a time, score tracking for practice sessions.

### Tournament Results
Not implemented. Upload and store tournament results, team scores, rankings.

### Comments & Ratings
Not implemented. User comments on questions, rating system for questions and packages, favorites.

### Email Notifications
Placeholder implementation. Will include real SMTP, password reset, registration confirmation.

### Package Access Levels
Not yet implemented. Planned access levels: Private, EditorsOnly, RegisteredUsersOnly, Public.

---

## Project Structure

```
/QuestionsHub.Blazor/
├── Components/
│   ├── Account/           # Authentication pages (Login, Register, Profile, LoginDisplay, etc.)
│   ├── Layout/            # Layout components (MainLayout, NavMenu, TopSearchBar)
│   ├── Pages/             # Main pages
│   │   ├── Admin/         # Admin pages (Editors, Users)
│   │   ├── Home.razor     # Home page with package list
│   │   ├── PackageDetail.razor  # Package view page
│   │   ├── ManagePackages.razor # Package management list
│   │   ├── ManagePackageDetail.razor # Package editor
│   │   ├── EditorProfile.razor  # Author/editor profile page
│   │   └── Search.razor   # Search page
│   ├── AddAuthorModal.razor # Modal for creating new authors
│   ├── AuthorSelector.razor # Multi-select autocomplete for authors/editors
│   ├── QuestionCard.razor # Question display component
│   └── TourNavigation.razor # Tour sidebar navigation
├── Controllers/           # API controllers
│   ├── AuthController.cs  # Authentication endpoints
│   ├── MediaController.cs # Media upload/delete API
│   └── Dto/               # Data transfer objects
├── Data/                  # Database context, seeding, migrations
├── Domain/                # Domain models (User, Package, Tour, Question, Author)
└── Infrastructure/        # Utilities and services
    ├── AuthorUserLinkingService.cs  # Author-User linking operations
    ├── MediaService.cs    # Media file handling
    └── SearchService.cs   # Full-text search
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
| Author profile page | ✅ Working |
| Author-User linking | ✅ Working |
| Search | ✅ Working |
| Admin: view editors list | ✅ Working |
| Admin: view all users | ✅ Working |
| Admin: promote user to editor | ✅ Working |
| Admin: demote editor | ✅ Working |
| Package access levels | ❌ Not implemented |
| Interactive play mode | ❌ Not implemented |
| Comments/ratings | ❌ Not implemented |

---

## Version History

| Date | Version | Changes |
|------|---------|---------|
| Jan 5, 2026 | 1.4 | Admin user management: editors list, users list, promote/demote editors, Author-User linking, enhanced editor profile page |
| Jan 4, 2026 | 1.3 | Added Authors as separate entity with many-to-many relationships, AuthorSelector component, EditorProfile page placeholder |
| Jan 2026 | 1.2 | Removed unused Package Management REST API (Blazor uses DbContext directly) |
| Jan 2026 | 1.1 | Added Preamble to Package and Tour, removed Tour Title, Media upload implemented |
| Dec 2025 | 1.0 | Initial specification document |
