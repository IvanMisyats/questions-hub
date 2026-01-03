# Uploads Setup Guide

This guide explains how to set up and manage uploaded files (images, videos, audio, packages) for the Questions Hub application.

## Overview

Uploaded files are stored on the VPS filesystem and mounted into Docker containers. This approach:
- Keeps uploaded files outside the container for easy management
- The `/handouts/` folder stores question media (publicly accessible via `/media/` URL)
- The `/packages/` folder stores original package files (not publicly accessible)
- Allows direct file management via SFTP/SCP
- Supports future CDN integration

## VPS Setup

### 1. Create Uploads Directory Structure

On your VPS, create the uploads directory:

```bash
# Create uploads directories
mkdir -p ~/questions-hub/uploads/handouts
mkdir -p ~/questions-hub/uploads/packages

# Verify structure
tree ~/questions-hub/uploads
```

Expected structure:
```
~/questions-hub/uploads/
├── handouts/         # Question media (images, audio, video) - publicly accessible
└── packages/         # Original package files (docx, pdf) - not publicly accessible
```

### 2. Set Proper Permissions

Set secure permissions on upload directories:

```bash
# Set directory permissions (read + execute for traversal)
chmod 755 ~/questions-hub/uploads

# Both folders need write permission for the application
chmod 755 ~/questions-hub/uploads/handouts
chmod 755 ~/questions-hub/uploads/packages
chown -R github-actions:github-actions ~/questions-hub/uploads
```

**Important:** 
- Never set execute permissions (`x`) on media files. This prevents malicious scripts from being executed even if uploaded.
- Both `handouts/` and `packages/` folders must be writable by the application.

### 3. Configure Environment Variable

Set the `UPLOADS_PATH` environment variable for Docker Compose:

```bash
# Add to your .env file or export before running docker compose
export UPLOADS_PATH=~/questions-hub/uploads
```

Or create a `.env` file in the project root:

```env
UPLOADS_PATH=~/questions-hub/uploads
POSTGRES_ROOT_PASSWORD=your_secure_password
QUESTIONSHUB_PASSWORD=your_secure_password
```

## Local Development Setup

### Create Local Uploads Folder

```powershell
# From project root (PowerShell)
mkdir uploads\handouts
mkdir uploads\packages
```

Or on Unix-like systems:

```bash
mkdir -p uploads/handouts uploads/packages
```

The application will automatically detect and serve handout files when running locally.

### Git Ignore

The `uploads/` folder should be in `.gitignore` to avoid committing large files to the repository:

```gitignore
# Uploaded files
uploads/
```

## Supported File Formats

### Images
- **Extensions:** `.jpg`, `.jpeg`, `.png`, `.gif`, `.webp`
- **MIME Types:** `image/jpeg`, `image/png`, `image/gif`, `image/webp`
- **Recommended Size:** Max 5MB per file
- **Use Cases:** Handout materials, answer illustrations

### Videos
- **Extensions:** `.mp4`, `.webm`
- **MIME Types:** `video/mp4`, `video/webm`
- **Recommended Size:** Max 50MB per file, 1280x720px (720p) recommended
- **Codecs:** H.264 for MP4, VP8/VP9 for WebM
- **Use Cases:** Video questions, video handouts

### Audio
- **Extensions:** `.mp3`, `.ogg`, `.wav`
- **MIME Types:** `audio/mpeg`, `audio/ogg`, `audio/wav`
- **Recommended Size:** Max 10MB per file
- **Bitrate:** 128-192 kbps recommended for MP3
- **Use Cases:** Audio questions, music identification

## Security Considerations

### File System Security

1. **Controlled Write Access:** 
   - `/uploads/` folder is writable for the media upload feature
   - Uploaded files get cryptographically random names (256-bit entropy)

2. **No Execute Permissions:** Files have 644 permissions
   - Owner: read/write
   - Group: read only
   - Others: read only
   - Execute bit never set

3. **Extension Whitelist:** Only allowed extensions are served
   - Application validates file extensions on upload and serving
   - Requests for `.php`, `.sh`, `.exe`, etc. return 404

4. **MIME Type Validation:** Content-Type headers match extensions
   - Prevents MIME type confusion attacks
   - `X-Content-Type-Options: nosniff` header prevents browser MIME sniffing

### Best Practices

- **Scan uploads:** Use ClamAV or similar for virus scanning (future feature)
- **File size limits:** Enforce maximum file sizes in upload interface
- **Regular audits:** Periodically review media directory contents
- **Backup media:** Include media directory in backup strategy
- **Monitor disk space:** Set up alerts for disk usage

## Nginx Configuration (Optional)

If using Nginx as a reverse proxy, you can optimize media serving:

```nginx
server {
    listen 80;
    server_name your-domain.com;

    # Application
    location / {
        proxy_pass http://localhost:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }

    # Direct media serving (bypasses ASP.NET Core for better performance)
    location /media/ {
        alias /home/github-actions/questions-hub/uploads/handouts/;
        
        # Security headers
        add_header X-Content-Type-Options "nosniff" always;
        add_header Content-Disposition "inline" always;
        
        # Cache for 1 year
        expires 1y;
        add_header Cache-Control "public, immutable";
        
        # Disable execution
        location ~* \.(php|sh|exe|bat|cmd|com|pif|src|asp|aspx|jsp)$ {
            deny all;
        }
    }
}
```

**Note:** If serving directly via Nginx, the application's static file middleware will be bypassed, but security is still maintained at the Nginx level.

## Troubleshooting

### Media Files Not Loading

1. **Check file permissions:**
   ```bash
   ls -la ~/questions-hub/uploads/handouts/
   ```
   Should show `rw-r--r--` (644) for files

2. **Verify mount in container:**
   ```bash
   docker exec questions-hub-web ls -la /app/uploads/handouts/
   ```

3. **Review logs:**
   ```bash
   docker logs questions-hub-web
   ```

### Permission Denied Errors

- Ensure directory has execute permission for traversal: `chmod 755`
- Ensure files have read permission: `chmod 644`
- Check SELinux/AppArmor policies if enabled

### Large Files Not Loading

- Check Docker memory limits in `docker-compose.yml`
- Verify disk space: `df -h`
- Consider using Nginx for direct serving of large files

## Media Upload Feature

Editors and Admins can upload media files directly through the package management interface.

### Accessing Media Upload

1. Navigate to **Manage Packages** → Select a package
2. Expand a tour and click on a question to edit
3. Use the file upload controls for:
   - **Роздатковий матеріал** (Handout) - Material shown before the question
   - **Ілюстрація до коментаря** (Comment Attachment) - Material shown with the answer

### Upload Limits

File size limits are configurable in `appsettings.json`:

```json
{
  "MediaUpload": {
    "MaxImageSizeBytes": 5242880,    // 5 MB for images
    "MaxVideoSizeBytes": 52428800,   // 50 MB for videos  
    "MaxAudioSizeBytes": 10485760    // 10 MB for audio
  }
}
```

### Security Features

- **Secure Filenames:** Uploaded files are renamed with cryptographically random names (256-bit entropy)
- **Extension Whitelist:** Only allowed media types can be uploaded
- **Path Traversal Protection:** Prevents directory escape attacks
- **Authorization:** Only authenticated Editors/Admins can upload
- **Ownership Validation:** Users can only upload to packages they own (Admins can access all)

### API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/media/questions/{id}/handout` | Upload handout media |
| POST | `/api/media/questions/{id}/comment` | Upload comment attachment |
| DELETE | `/api/media/questions/{id}/handout` | Delete handout media |
| DELETE | `/api/media/questions/{id}/comment` | Delete comment attachment |

## Future Enhancements

### Planned Features
- Automatic thumbnail generation for images
- Video transcoding for optimal web delivery
- CDN integration for better performance
- Media usage tracking and analytics
- Orphaned media cleanup tools

### CDN Integration

When ready to use a CDN:

1. Upload media to CDN (S3, Cloudflare, etc.)
2. Update database URLs to CDN URLs
3. Keep local copy as fallback
4. Update `Program.cs` to check CDN first

## Summary

- **Location:** `~/questions-hub/uploads/` on VPS
- **Structure:** `handouts/` for public media, `packages/` for original files
- **Permissions:** 755 for directories, 644 for files
- **Security:** Extension whitelist, no execute, MIME validation
- **Formats:** Images (jpg, png, gif, webp), Videos (mp4, webm), Audio (mp3, ogg, wav)

