﻿# Authentication & Authorization

## Overview

QuestionsHub implements role-based authentication and authorization using **ASP.NET Core Identity** with PostgreSQL as the backing store.

**Last Updated**: January 5, 2026

---

## User Roles

### 1. Anonymous (Unauthenticated)
- Can view public pages
- Can access packages marked as "Everyone (including anonymous)"
- No registration required

### 2. User (Default Role)
- **Assigned to**: All newly registered users (auto-approved)
- **Permissions**: 
  - Read-only access to packages
  - View packages marked for "Registered users"
  - View own profile
  - Edit own profile

### 3. Editor
- **Assigned by**: Admin promotion
- **Permissions**: 
  - All User permissions
  - Create new question packages
  - Edit own packages
  - View editors list (read-only)
  - Linked to an Author entity (created automatically on promotion)
  
> **Note**: When a user is promoted to Editor, an Author entity is automatically created and linked. If an Author with the same name already exists, it is linked instead. When demoted, the Author entity remains for historical data.

### 4. Admin
- **Assigned at**: System initialization via `.env`
- **Permissions**: 
  - All Editor permissions
  - Promote users to Editor role (`/admin/users`)
  - Demote Editors to User role (`/admin/editors`)
  - View all users with email addresses
  - Link/unlink Author ↔ User manually (`/editor/{id}`)
  - Manage all packages regardless of owner

---

## Registration Flow

### User Registration
1. User fills registration form with:
   - **Ім'я** (First Name) - required
   - **Прізвище** (Last Name) - required
   - **Місто** (City) - optional
   - **Команда** (Team) - optional
   - **Email** - required, unique
   - **Пароль** (Password) - required, validated
2. Password is validated against security policy
3. Account is **auto-approved** (no admin approval needed)
4. **Email confirmation required** - User receives confirmation email
5. User clicks confirmation link to activate account
6. User can login with "User" role after confirmation

### Future Enhancement
Manual admin approval of registrations is planned but not yet implemented.

---

## Authentication Features

### ✅ Implemented
- **Remember Me**: Persistent authentication cookie (configurable lifetime)
- **Account Lockout**: 5 failed login attempts → 15-minute lockout
- **Password Security**: Salted and hashed using PBKDF2 (Identity default)
- **Session Timeout**: Configurable cookie expiration
- **CSRF/XSRF Protection**: Automatic via Blazor Server
- **Password Reset**: 24-hour token validity, sent via email
- **Edit Profile**: Users can update their information
- **Email Confirmation**: Required for new registrations
- **Forgot Password**: Email-based password reset

### ❌ Excluded (Current Scope)
- Two-factor authentication (2FA)
- Social login (Google, Facebook, etc.)

---

## Security Configuration

### Password Policy
- Minimum length: 8 characters
- Requires: digit, lowercase, uppercase, non-alphanumeric
- Password reset token expires after 24 hours

### Account Lockout
- Max failed attempts: 5
- Lockout duration: 15 minutes
- Applies to password and password reset attempts

### Session Management
- Cookie-based authentication (Blazor Server)
- Configurable session timeout
- Sliding expiration supported

---

## Privacy

### Email Visibility
User email addresses are protected and only visible to Admins:

| Page | Who can see email |
|------|-------------------|
| `/admin/editors` | Admin only |
| `/admin/users` | Admin only |
| `/editor/{id}` | Admin only |

### City Visibility
User city is visible to all authenticated users on:
- Editors list (`/admin/editors`)
- Editor profile page (`/editor/{id}`)

### Role Change Notification
When a user is promoted or demoted, they must **log out and log back in** for the role change to take effect. This is because role claims are cached in the authentication cookie.

---

## Admin Bootstrapping

### First Admin User
The initial admin user is created during application startup from environment variables:

```env
ADMIN_EMAIL=admin@example.com
ADMIN_PASSWORD=SecurePassword123!
```

**Important**: 
- Application **will fail to start** if these variables are missing
- This is intentional to prevent accidental deployment without admin access
- Admin credentials should be changed immediately after first login

### Adding More Admins
Currently, there is only one admin user (the bootstrapped one). Additional admins cannot be created through the UI in the current scope.

**Future Enhancement**: Admin can promote other users to Admin role.

---

## Database Schema

### ApplicationUser Table
Extends `IdentityUser` with custom fields:

| Column | Type | Required | Description |
|--------|------|----------|-------------|
| Id | string | ✓ | Primary key (GUID) |
| UserName | string | ✓ | Same as Email |
| Email | string | ✓ | Unique email address |
| EmailConfirmed | bool | ✓ | Always true (no email verification) |
| PasswordHash | string | ✓ | Salted hash (PBKDF2) |
| FirstName | string | ✓ | Ім'я |
| LastName | string | ✓ | Прізвище |
| City | string | | Місто (optional) |
| Team | string | | Команда (optional) |
| AuthorId | int? | | Linked Author entity (for Editors) |
| AccessFailedCount | int | ✓ | Failed login attempts |
| LockoutEnd | DateTimeOffset? | | When lockout expires |

### Author Table
Stores question/tour authors, can be linked to users:

| Column | Type | Required | Description |
|--------|------|----------|-------------|
| Id | int | ✓ | Primary key |
| FirstName | string | ✓ | Ім'я |
| LastName | string | ✓ | Прізвище |
| UserId | string? | | Linked ApplicationUser (optional) |

**Constraints**: Unique index on (FirstName, LastName)

### Identity Tables
Standard ASP.NET Core Identity tables:
- `AspNetUsers` - User accounts
- `AspNetRoles` - Roles (Admin, Editor, User)
- `AspNetUserRoles` - User-to-role mapping
- `AspNetUserClaims` - User claims
- `AspNetUserLogins` - External logins (not used)
- `AspNetUserTokens` - Password reset tokens
- `AspNetRoleClaims` - Role claims

---

## Package Access Control

> **Note**: Package access levels are **not yet implemented**. Currently, packages have three statuses:
> - **Draft** - Only visible to owner and admins
> - **Published** - Visible to all users
> - **Archived** - Hidden from main list, accessible via direct link

### Planned Access Levels (Future)
Editors will be able to set package visibility when creating/editing packages:

1. **Private (Приватний)**
   - Only the package creator can view
   - Hidden from search and listings

2. **Editors Only (Тільки редактори)**
   - Only users with Editor role can view
   
3. **Registered Users (Зареєстровані користувачі)**
   - Requires login
   - Available to User, Editor, and Admin roles

4. **Everyone/Public (Усі)**
   - Public access
   - Anonymous users can view


---

## UI Components

### Login Display
Located in header, shows:
- **Anonymous**: "Увійти" and "Реєстрація" buttons
- **Authenticated**: User's full name with dropdown menu:
  - Мій профіль
  - Мої пакети (Editor/Admin only)
  - Редактори (Editor/Admin only)
  - Користувачі (Admin only)
  - Вийти

### Account Pages
- `/Account/Login` - Login page with "Remember Me"
- `/Account/Register` - Registration form
- `/Account/Logout` - Logout handler
- `/Account/ForgotPassword` - Request password reset
- `/Account/ResetPassword` - Set new password with token
- `/Account/Profile` - View/edit user profile
- `/Account/AccessDenied` - Unauthorized access page

### Admin Pages
- `/admin/editors` - List of all editors (read-only for Editors, manageable for Admin)
- `/admin/users` - List of all users with promote/demote actions (Admin only)
- `/editor/{id}` - Author profile page with user linking (Admin can link/unlink)

---

## API Usage Examples

### Check Current User
```csharp
@inject AuthenticationStateProvider AuthenticationStateProvider

var authState = await AuthenticationStateProvider.GetAuthenticationStateAsync();
var user = authState.User;

if (user.Identity?.IsAuthenticated ?? false)
{
    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var email = user.Identity.Name;
    bool isAdmin = user.IsInRole("Admin");
    bool isEditor = user.IsInRole("Editor");
}
```

### Authorize in Razor Components
```razor
@attribute [Authorize(Roles = "Admin")]

<AuthorizeView Roles="Editor,Admin">
    <Authorized>
        <button>Створити пакет</button>
    </Authorized>
    <NotAuthorized>
        <p>Доступ заборонено</p>
    </NotAuthorized>
</AuthorizeView>
```

### Check Access in Code
```csharp
@inject IAuthorizationService AuthorizationService

var result = await AuthorizationService.AuthorizeAsync(user, "RequireEditor");
if (result.Succeeded)
{
    // User is Editor or Admin
}
```

---

## Localization

All authentication UI is in **Ukrainian**:
- Form labels
- Validation messages
- Error messages
- Button text
- Email templates

See implementation for complete translation mapping.

---

## Email Service

### Implementation
Email sending is implemented using **Mailjet** API:

**Provider**: Mailjet (https://www.mailjet.com/)
**Integration**: `Mailjet.Api` NuGet package
**Class**: `MailjetEmailSender` implementing `IEmailSender<ApplicationUser>`

### Email Types
1. **Email Confirmation** - Sent after registration to verify email address
2. **Password Reset** - Sent when user requests password reset

### Configuration
Email settings are configured in `appsettings.json` and overridden via environment variables:

```json
{
  "Email": {
    "ApiKey": "",
    "ApiSecret": "",
    "SenderEmail": "noreply@questions.com.ua",
    "SenderName": "База запитань ЩДК",
    "SiteUrl": "https://questions.com.ua"
  }
}
```

**Environment Variables** (for production deployment):
- `EMAIL_API_KEY` - Mailjet API Key
- `EMAIL_API_SECRET` - Mailjet API Secret

### Sender Domain
The sender domain `questions.com.ua` must be verified in Mailjet for SPF/DKIM authentication.

---

## Security Considerations

### Best Practices Implemented
- ✅ Passwords salted and hashed (never stored in plain text)
- ✅ HTTPS enforced (should be configured in production)
- ✅ CSRF/XSRF tokens automatic in Blazor
- ✅ Account lockout prevents brute force
- ✅ Password reset tokens expire after 24 hours
- ✅ Email confirmation required for new accounts
- ✅ Secure cookie settings (HttpOnly, Secure in production)

### Recommendations for Production
1. **Use HTTPS**: Enforce HTTPS in production
2. **Rotate Admin Password**: Change default admin password immediately
3. **Configure Email**: Replace console email sender with real SMTP
4. **Monitor Failed Logins**: Log and alert on suspicious activity
5. **Backup Database**: Regular backups of user data
6. **Update Dependencies**: Keep Identity packages up to date
7. **Consider 2FA**: Enable for admin accounts in future

---

## Troubleshooting

### User Cannot Login
1. Check account lockout status (5 failed attempts)
2. Verify email/password are correct
3. Check if user role is assigned
4. Ensure cookies are enabled

### Admin Cannot Access Admin Panel
1. Verify user has "Admin" role in database
2. Check `AspNetUserRoles` table
3. Ensure admin was seeded correctly from `.env`

### Password Reset Not Working
1. Token expires after 24 hours
2. Check console logs for reset token (if email not configured)
3. Verify user email exists in database

---

## Migration Guide

### From No Auth to Identity

If you have existing data:
1. Backup database
2. Run Identity migrations
3. Seed roles and admin user
4. Existing anonymous data remains accessible

### Database Migration Command
```bash
dotnet ef migrations add AddIdentity
dotnet ef database update
```

---

## Future Enhancements

### Planned
- Package access levels (Private, EditorsOnly, RegisteredUsersOnly, Public)
- Real email sending (SMTP configuration)
- Activity logging (last login, actions)
- Multiple admin users (Admin can promote others to Admin)

### Implemented (Jan 2026)
- ✅ Admin can promote users to Editor role
- ✅ Admin can demote Editors to User role
- ✅ Author-User linking (automatic on promotion, manual via admin)
- ✅ Editors list page (visible to Editor and Admin)
- ✅ Users list page with search (Admin only)

### Possible
- Two-factor authentication (2FA)
- Social login (Google, Facebook)
- Account deletion (GDPR compliance)
- Password strength indicator in UI
- Terms of Service acceptance
- Privacy policy

---

## Support

For issues or questions:
1. Check this documentation
2. Review code comments in `Components/Account/`
3. Consult ASP.NET Core Identity documentation
4. Check application logs for error details

