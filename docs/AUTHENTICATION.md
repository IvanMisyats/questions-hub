# Authentication & Authorization

## Overview

QuestionsHub implements role-based authentication and authorization using **ASP.NET Core Identity** with PostgreSQL as the backing store.

**Last Updated**: December 25, 2025

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
  - Set package access levels:
    - Private (nobody)
    - By link (anyone with link)
    - Registered users only
    - Everyone (including anonymous)

### 4. Admin
- **Assigned at**: System initialization via `.env`
- **Permissions**: 
  - All Editor permissions
  - Promote users to Editor role
  - Demote Editors to User role
  - Manage all users
  - Access admin panel

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
4. **No email confirmation required**
5. User can immediately login with "User" role

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
- **Password Reset**: 24-hour token validity
- **Edit Profile**: Users can update their information

### ❌ Excluded (Current Scope)
- Email confirmation (auto-approved registrations)
- Two-factor authentication (2FA)
- Social login (Google, Facebook, etc.)
- Email sending (placeholder implementation for now)

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
| AccessFailedCount | int | ✓ | Failed login attempts |
| LockoutEnd | DateTimeOffset? | | When lockout expires |

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

### Access Levels
Editors can set package visibility when creating/editing packages:

1. **Private (Приватний)**
   - Only the package creator can view
   - Hidden from search and listings

2. **By Link (За посиланням)**
   - Anyone with the direct URL can access
   - Not listed publicly

3. **Registered Users (Зареєстровані користувачі)**
   - Requires login
   - Available to User, Editor, and Admin roles

4. **Everyone (Усі)**
   - Public access
   - Anonymous users can view

### Implementation
Package access control is enforced at:
- Page level: `@attribute [Authorize]` or custom policies
- Component level: `<AuthorizeView>` components
- Service level: Check user roles and package settings

---

## UI Components

### Login Display
- Replaces "Слава Україні!" in the header
- Shows:
  - **Anonymous**: "Увійти" button
  - **Authenticated**: User's full name + "Війти" button

### Pages
- `/Account/Login` - Login page with "Remember Me"
- `/Account/Register` - Registration form
- `/Account/Logout` - Logout handler
- `/Account/ForgotPassword` - Request password reset
- `/Account/ResetPassword` - Set new password with token
- `/Account/EditProfile` - Edit user profile
- `/Account/AccessDenied` - Unauthorized access page
- `/Admin/Users` - User management (Admin only)
- `/Admin/UserEdit/{id}` - Promote/demote user (Admin only)

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
- Email templates (when implemented)

See implementation for complete translation mapping.

---

## Email Service

### Current Implementation
`IEmailSender` is implemented as a placeholder that logs to console:
- Email confirmation (not used, as confirmation is disabled)
- Password reset emails (logs token to console)

### Future Enhancement
Implement real SMTP email sending:
- Configure SendGrid, Mailgun, or custom SMTP
- Add email templates
- Send actual emails instead of console logging

---

## Security Considerations

### Best Practices Implemented
- ✅ Passwords salted and hashed (never stored in plain text)
- ✅ HTTPS enforced (should be configured in production)
- ✅ CSRF/XSRF tokens automatic in Blazor
- ✅ Account lockout prevents brute force
- ✅ Password reset tokens expire after 24 hours
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
- Manual admin approval of registrations
- Multiple admin users
- Admin can promote users to Admin role
- Real email sending (SMTP configuration)
- Activity logging (last login, actions)

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

