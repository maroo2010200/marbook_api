# Marbook — Full Project Architecture & Master Roadmap

**Project:** Marbook — A modern, full-featured Facebook-like social media platform  
**Backend:** ASP.NET Core Web API (.NET 10) + EF Core + PostgreSQL (`E:\MarbookApi`)  
**Frontend:** React 19 + TypeScript + Vite (`E:\marbook-react`)  
**Repositories:**
- Backend: [marbook_api](https://github.com/maroo2010200/marbook_api)
- Frontend: [marbook_react](https://github.com/maroo2010200/marbook_react)

---

## 1. System Architecture & Tech Stack

```
┌────────────────────────────────────────────────────────┐
│                   React + Vite (Frontend)              │
│       React 19 • TypeScript • Vite • React Router v7   │
│             Axios Interceptors • CSS-in-JS/Vanilla     │
└──────────────────────────┬─────────────────────────────┘
                           │ HTTP / REST (JWT Bearer)
                           ▼
┌────────────────────────────────────────────────────────┐
│               ASP.NET Core Web API (.NET 10)           │
│        Controllers • DTOs • BCrypt • JWT Service       │
│        Global Exception Handler • Brevo SMTP API       │
└──────────────────────────┬─────────────────────────────┘
                           │ EF Core 10 (Npgsql)
                           ▼
┌────────────────────────────────────────────────────────┐
│                   PostgreSQL Database                  │
│       Tables with Triggers • pg_trgm • GIN Indexes     │
└────────────────────────────────────────────────────────┘
```

| Area | Backend | Frontend |
|---|---|---|
| **Runtime & Framework** | .NET 10 (C# 14) / ASP.NET Core Web API | Node.js / React 19 / Vite |
| **Language** | C# | TypeScript (Strict mode) |
| **Database & ORM** | PostgreSQL + EF Core (Npgsql 10.0.3) | — |
| **Auth & Security** | JWT Bearer, BCrypt.Net-Next, Claims identity | Axios interceptor, AuthContext |
| **Email Service** | Brevo SMTP API (Transaction emails) | — |
| **Search & Indexing** | PostgreSQL Full-Text Search (FTS) + `pg_trgm` | Live search debouncing |
| **Routing & State** | REST API Controllers | React Router DOM v7, Custom hooks |

---

## 2. What Is Done (Completed Phases)

### ✅ Phase 1: Authentication & Account Security (100% Complete)
- **Backend**:
  - Secure registration (`POST /api/auth/register`) with BCrypt password hashing.
  - Login endpoint (`POST /api/auth/login`) issuing signed JWT tokens.
  - Verification token generation and delivery via **Brevo SMTP API**.
  - Single-use, time-expiring verification tokens (`ExpiresAt > UtcNow`, `IsUsed`).
  - Account activation endpoint (`GET /api/auth/verify?token=...`).
  - Resend verification email endpoint (`POST /api/auth/resend-verification`).
  - **Base64 Percent-Encoding Fix**: Addressed email token corruption where `+` was decoded into spaces by url-encoded parsers (`Uri.EscapeDataString`).
  - Unverified accounts locked out of protected endpoints with `401 Unauthorized`.
- **Frontend**:
  - `AuthContext`: Centralized auth state (`user`, `token`, `isAuthenticated`, `login`, `logout`, `verify`).
  - `LoginPage`: Form validation, 401 unverified account interception, and inline "Resend verification email" action.
  - `RegisterPage`: Validation, auto-login prevention on unverified state, and registration confirmation screen.
  - `VerifyPage`: Full-page route (`/verify?token=...`) with axios error handling and auto-redirect.
  - `ProtectedRoute`: Route guard restricting feed access to authenticated users.

---

### ✅ Phase 2: Feed, Posts, Likes & Nested Comments (100% Complete)
- **Posts Feed**:
  - **Backend**: Paginated feed (`GET /api/posts`), keyword search, full-text search (`GET /api/posts/search`), post creation (`POST /api/posts`), and single post lookup (`GET /api/posts/{id}`).
  - **Frontend**: `FeedPage` with infinite scrolling via `IntersectionObserver` sentinel, loading skeletons, and "You're up to date" end-of-stream indicator.
  - **Encapsulated State**: `usePosts` hook encapsulated with `updatePost(postId, changes)` to prevent outside state corruption.
- **Likes System**:
  - **Backend**: Post like toggle (`POST /api/posts/{id}/likes`), comment like toggle (`POST /api/comments/{id}/Likes`). DTOs return `LikesCount`, `LikeCount`, and user-specific `IsLiked`/`Liked` booleans.
  - **Frontend**: Instagram/X-style action rows with count badges next to icons (hidden when 0), optimistic UI updates with automatic rollback on network error.
- **Nested Comments & Thread Tree**:
  - **Backend**:
    - Root comments paginated endpoint (`GET /api/posts/{postId}/comments`).
    - Nested replies endpoints (`POST /api/comments/{commentId}/Replies`, `GET /api/comments/{commentId}/Replies`).
    - Self-referencing EF Core relationship (`ParentCommentId`) with cascade handling.
    - **Total Post Comments**: `PostsController` returns `CommentsCount = p.Comments.Count()`, including all nested replies.
    - **Recursive Thread Replies**: In-memory `CountAllDescendants(id)` accurately calculates total descendant replies for root and nested comments.
  - **Frontend**:
    - `CommentsModal`: Instagram/Facebook-style modal dialog with post author header, post date, and caption preview.
    - **Infinite scroll** on root comments with page-size chunking.
    - **Nested Reply Tree**: YouTube/Facebook-style branch connectors with elbow curved lines and vertical branch guides.
    - **Inline Reply Input Banner**: Replying indicator with `@username` tag and cancel button.
    - **Ancestor Count Propagation**: `addReplyAndIncrementAncestors` updates thread reply counts up the ancestor chain in real time.
    - **Modal Stability**: Keyed effect on `postId` to eliminate re-fetch flashing and scroll jumps when commenting.

---

## 3. What's Coming Next (Full Project Roadmap)

```
┌────────────────────────────────────────────────────────────────────────┐
│                       MARBOOK MASTER ROADMAP                           │
├──────────────────────────┬─────────────────────────────┬───────────────┤
│ Phase                    │ Features                    │ Priority      │
├──────────────────────────┼─────────────────────────────┼───────────────┤
│ Phase 3: Post & Comment  │ "What's on your mind?" card │ 🟢 COMPLETED  │
│          Lifecycle (CRUD)│ Post/Comment Edit & Delete  │               │
├──────────────────────────┼─────────────────────────────┼───────────────┤
│ Phase 4: User Profiles   │ Profile page, bio/avatar,   │ 🟢 COMPLETED  │
│          & Follow System │ follow button, feed filter  │               │
├──────────────────────────┼─────────────────────────────┼───────────────┤
│ Phase 5: Media & Rich    │ Post image attachments,     │ 🟢 COMPLETED  │
│          Attachments     │ avatar uploads, lightbox    │               │
├──────────────────────────┼─────────────────────────────┼───────────────┤
│ Phase 6: Notifications   │ In-app activity feed,       │ 🟢 COMPLETED  │
│          System          │ unread badge, SignalR push  │               │
├──────────────────────────┼─────────────────────────────┼───────────────┤
│ Phase 7: Real-Time Chat  │ 1-on-1 direct messaging,    │ 🟢 COMPLETED  │
│          & Direct DMs    │ SignalR hubs, chat drawer   │               │
├──────────────────────────┼─────────────────────────────┼───────────────┤
│ Phase 8: Performance &   │ Redis caching, rate limits, │ ⚪ REMAINING  │
│          Production      │ mobile responsive polish    │ (FINAL PHASE) │
└──────────────────────────┴─────────────────────────────┴───────────────┘
```

---

### ✅ Phase 3: Complete Post & Comment Lifecycle (Completed)
*Goal: Allow users to create, edit, and delete their own posts and comments directly from the UI.*

1. **"What's on your mind?" Composer Card**:
   - Pinned at the top of `FeedPage` for authenticated users.
   - Quick input card opening an expanded Post Creator modal or inline editor.
   - Character counter and "Post" button with loading/disabled states.
   - Optimistic prepending of newly created posts to the feed stream.
2. **Post Management (Edit & Delete)**:
   - **Backend**: `PUT /api/posts/{id}` and `DELETE /api/posts/{id}` (ownership validated: `post.UserId == currentUserId`).
   - **Frontend**: Three-dots menu (`...`) on post cards for the post author.
   - Edit mode (inline or modal) updating post content.
   - Confirmation dialog before deleting a post, removing it from the feed state instantly.
3. **Comment Management (Edit & Delete)**:
   - **Backend**: `PUT /api/comments/{id}` and `DELETE /api/comments/{id}` with cascade deletion of descendant replies.
   - **Frontend**: Contextual menu on comments allowing authors to edit or remove their comments, automatically updating ancestor thread counts.

---

### ✅ Phase 4: User Profiles & Follow System (Completed)
*Goal: Give each user a personal profile and create a personalized social graph.*

1. **User Profile Page (`/profile/:id` or `@username`)**:
   - Header with user avatar, name, username, join date, and bio.
   - Follower and Following count metrics.
   - Profile tabs: "Posts" (all posts created by this user) and "About".
2. **Profile Editing**:
   - `PUT /api/users/me` endpoint to update display name, bio, and birthdate.
   - Edit profile modal on user's own profile page.
3. **Follow / Unfollow System**:
   - Backend: Wire existing follow entities (`Follows` table) with `POST /api/users/{id}/follow` and `DELETE /api/users/{id}/follow`.
   - Frontend: Follow/Unfollow toggle button on profile headers and post cards.
4. **Personalized Feed Filter**:
   - Tab switcher at top of Feed: "For You" (global posts) vs "Following" (posts only from followed users).
5. **Post Audience & Visibility Controls (Public, Followers only, Private)**:
   - **Backend**: `PostVisibility` enum (`Public = 0, Followers = 1, Private = 2`) with JSON string conversion and database migration.
   - Visibility filtering in `PostsController`:
     - Anonymous users can only view `Public` posts.
     - Authenticated users can view their own posts (including `Private`), all `Public` posts, and `Followers`-only posts from followed authors.
     - Single post lookup `GetPost(id)` and full-text search enforce audience access.
   - **Frontend**:
     - Interactive audience dropdown in `PostComposer` replacing static badge with Public (`🌐`), Followers only (`👥`), and Only Me (`🔒`) selector.
     - Audience indicator badge with icon and tooltip rendered next to post date in `PostCard`.

---

### ✅ Phase 5: Media & Rich Attachments (Completed)
*Goal: Support photo sharing in posts and custom user profile avatars.*

1. **Media Upload Pipeline (`POST /api/media/upload`)**:
   - Multi-layer security: 10MB file limit, extension whitelist, MIME check, binary magic-byte inspection (JPEG, PNG, GIF, WebP), and GUID renaming.
   - Static files serving via ASP.NET Core `UseStaticFiles`.
2. **Post Image Attachments & Canvas Adjuster**:
   - Zero-dependency canvas image adjuster (`ImageAdjustModal.tsx`) with aspect ratio clipping (1:1, 4:5, 16:9), non-passive mouse wheel zoom, and pan boundary clamping.
   - Dynamic Facebook-style multi-image grid (`PostImagesGrid.tsx`) adapting for 1, 2, 3, 4, or 5+ photos with frosted `+N` overlay.
3. **Fullscreen Lightbox**:
   - Reusable `LightboxModal.tsx` supporting keyboard navigation (Left/Right/Esc), image download/open in tab, and scroll locking.
4. **Custom Avatars & Cover Photos**:
   - Avatar uploads from profile page with automatic circular cropping preview and instant cache busting.

---

### ✅ Phase 6: Notifications System (Completed)
*Goal: Keep users engaged when others interact with their content.*

1. **Notification Schema & Service**:
   - PostgreSQL `Notifications` table with composite indexes on `RecipientId`, `IsRead`, and `CreatedAt`.
   - Event types: `PostLiked`, `CommentLiked`, `CommentCreated`, `ReplyCreated`, `UserFollowed`.
   - Self-action suppression (`recipientId == senderId`).
   - Endpoints: `GET /api/notifications`, `PUT /api/notifications/{id}/read`, `PUT /api/notifications/read-all`.
2. **SignalR Real-Time Push (`NotificationHub.cs`)**:
   - Mounted at `/hubs/notifications`.
   - Injects `IHubContext<NotificationHub>` to push `ReceiveNotification` events directly to `Clients.User(recipientId)`.
3. **Frontend Notifications UI**:
   - Bell icon in `Navbar` with live unread badge.
   - Dropdown drawer with actor avatars, event descriptions, relative timestamps, and one-click navigation to content.

---

### ✅ Phase 7: Real-Time Direct Messaging (Chat) (Completed)
*Goal: Enable private 1-on-1 conversations between users.*

1. **Chat Infrastructure & SignalR Hub (`ChatHub.cs`)**:
   - Database tables: `Conversations`, `ConversationParticipants`, `Messages`.
   - `[Authorize]` SignalR Hub at `/hubs/chat` with JWT query string authentication (`OnMessageReceived`).
   - Room groups (`conversation_{id}`) with strict participant authorization guards.
2. **Dual-Delivery Message Dispatching Pattern**:
   - `POST /api/conversations/{id}/messages` persists messages via EF Core transactions.
   - Broadcasts to room group `Clients.Group` AND recipient user `Clients.User` with real-time `ConversationUpdated` summary payloads.
3. **Ephemeral Typing Indicators**:
   - Client throttling (2,000ms) and ephemeral in-memory hub forwarding without database write overhead.
   - Receiver 3,000ms auto-expiration with animated bouncing CSS dots (`@keyframes typingBounce`).
4. **Rich Chat Experience & Unread Watermark**:
   - Zero-dependency Curated Reaction `GifPicker.tsx` (Reactions, Agree, Happy, Clapping, Mindblown, etc.).
   - Regex URL tokenization, Giphy CDN conversion, pure media sticker layouts (borderless floating media), and Lightbox integration.
   - Watermark-based unread count via `ConversationParticipant.LastReadAt`.
   - Active thread ref tracking in `ChatContext.tsx` preventing phantom unread badges and infinite re-render loops.
   - Full message lifecycle: editing (`PUT`) and soft-deletion (`DELETE`) with live socket broadcast.

---

### 📋 Phase 8: Performance, Caching & Production Hardening
*Goal: Prepare the application for high concurrency and production deployment.*

1. **Caching Layer**:
   - Distributed caching with **Redis** for feed query caching and like/comment counters.
2. **Security & Rate Limiting**:
   - ASP.NET Core built-in rate limiting on auth endpoints (prevent brute-force).
   - Content sanitization (XSS protection on post and comment content).
3. **Frontend Polish & Mobile UX**:
   - ✅ **Dark Mode Theme System**: Global toggle with localStorage persistence, system preference auto-detection (`prefers-color-scheme`), CSS variable tokens, and smooth color transitions.
   - Mobile-first bottom navigation bar for phones.
   - Responsive sidebar layout with Facebook-style Left (navigation) and Right (contacts/trending) sidebars.
   - Global Toast notification system for feedback.
4. **DevOps & CI/CD**:
   - Dockerfile for backend API and frontend Nginx image.
   - GitHub Actions workflow running tests, linting, and automated builds.

---


---

### ✅ Phase 8: Marshmallow Rebrand, Settings Hub & Facebook Layout Engine (Completed)
*Goal: Rebrand platform to Marshmallow, introduce user settings & security, consolidate discovery search, and implement a Facebook-grade fixed & sticky responsive layout.*

- **Backend**:
  - Added `ChangePasswordDto` and `DeleteAccountDto` in `DTOs/AccountSecurityDtos.cs`.
  - Added `POST /api/users/change-password` with BCrypt current password validation and password rotation.
  - Added `POST /api/users/delete-account` with mandatory password confirmation, disk avatar cleanup, and cascading foreign key deletion across posts, comments, likes, follows, messages, and notifications.
- **Frontend Settings & Security**:
  - Created `SettingsPage.tsx` with 3 tabs:
    - **Account & Security**: Identity badge, Change Password form (show/hide eye toggles, length validation), Danger Zone (Delete Account confirmation modal).
    - **Appearance**: Visual card selection for "Toasted Cream" (Light) and "Campfire Cocoa" (Dark).
    - **Notifications**: Granular toggles for Likes, Comments, Mentions, and Direct Messages persisted in `localStorage`.
  - Registered protected route `/settings` and linked via Navbar Account Menu and Left Sidebar.
- **Unified Live Explore Search**:
  - Removed redundant navbar search bar to clean the header.
  - Upgraded Explore (`/search`) with 280ms debounced live instant search as you type (no Enter required).
  - Added dynamic Explore stream displaying recent posts and suggested creators when the search query is empty.
- **Facebook-Style Fixed & Sticky Responsive Layout**:
  - Fixed navbar (`position: fixed; top: 0; z-index: 1000;`) with backdrop blur.
  - Edge-to-edge column alignment: Left sidebar aligned with Marshmallow logo, right sidebar aligned with profile controls.
  - Sticky sidebars (`position: sticky; top: 68px;`) with safe vertical overflow.
  - Responsive icon-rail collapse: collapses into a 64px icon rail with circular action buttons and tooltips when window is minimized or snapped to half-screen.

## 4. Current Database Schema Overview

```sql
-- Users Table
Users (
    Id SERIAL PRIMARY KEY,
    Name VARCHAR,
    Email VARCHAR UNIQUE,
    Username VARCHAR UNIQUE,
    PasswordHash VARCHAR,
    Birthdate DATE,
    Bio TEXT,
    AvatarUrl VARCHAR,
    IsEmailVerified BOOLEAN DEFAULT FALSE,
    CreatedAt TIMESTAMPTZ,
    UpdatedAt TIMESTAMPTZ
);

-- Email Verifications Table
EmailVerifications (
    Id SERIAL PRIMARY KEY,
    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
    Token VARCHAR NOT NULL,
    ExpiresAt TIMESTAMPTZ NOT NULL,
    IsUsed BOOLEAN DEFAULT FALSE
);

-- Posts Table
Posts (
    Id SERIAL PRIMARY KEY,
    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
    Content TEXT NOT NULL,
    Visibility INT DEFAULT 0,
    CreatedAt TIMESTAMPTZ,
    UpdatedAt TIMESTAMPTZ
);

-- Post Likes Table
Likes (
    Id SERIAL PRIMARY KEY,
    PostId INT REFERENCES Posts(Id) ON DELETE CASCADE,
    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
    CreatedAt TIMESTAMPTZ,
    UpdatedAt TIMESTAMPTZ,
    UNIQUE (PostId, UserId)
);

-- Comments Table (Self-referencing tree)
Comments (
    Id SERIAL PRIMARY KEY,
    PostId INT REFERENCES Posts(Id) ON DELETE CASCADE,
    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
    ParentCommentId INT REFERENCES Comments(Id) ON DELETE CASCADE,
    Content TEXT NOT NULL,
    CreatedAt TIMESTAMPTZ,
    UpdatedAt TIMESTAMPTZ
);

-- Comment Likes Table
CommentLikes (
    Id SERIAL PRIMARY KEY,
    CommentId INT REFERENCES Comments(Id) ON DELETE CASCADE,
    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
    CreatedAt TIMESTAMPTZ,
    UpdatedAt TIMESTAMPTZ,
    UNIQUE (CommentId, UserId)
);

-- Follows Table (Social Graph)
Follows (
    Id SERIAL PRIMARY KEY,
    FollowerId INT REFERENCES Users(Id) ON DELETE CASCADE,
    FolloweeId INT REFERENCES Users(Id) ON DELETE CASCADE,
    CreatedAt TIMESTAMPTZ,
    UNIQUE (FollowerId, FolloweeId)
);

-- Post Images Table
PostImages (
    Id SERIAL PRIMARY KEY,
    PostId INT REFERENCES Posts(Id) ON DELETE CASCADE,
    ImageUrl VARCHAR NOT NULL,
    CreatedAt TIMESTAMPTZ
);

-- In-App Notifications Table
Notifications (
    Id SERIAL PRIMARY KEY,
    RecipientId INT REFERENCES Users(Id) ON DELETE CASCADE,
    SenderId INT REFERENCES Users(Id) ON DELETE CASCADE,
    Type INT NOT NULL,
    TargetPostId INT REFERENCES Posts(Id) ON DELETE CASCADE,
    TargetCommentId INT REFERENCES Comments(Id) ON DELETE CASCADE,
    IsRead BOOLEAN DEFAULT FALSE,
    CreatedAt TIMESTAMPTZ
);

-- Chat Conversations Table
Conversations (
    Id SERIAL PRIMARY KEY,
    CreatedAt TIMESTAMPTZ,
    UpdatedAt TIMESTAMPTZ
);

-- Conversation Participants Table
ConversationParticipants (
    Id SERIAL PRIMARY KEY,
    ConversationId INT REFERENCES Conversations(Id) ON DELETE CASCADE,
    UserId INT REFERENCES Users(Id) ON DELETE CASCADE,
    JoinedAt TIMESTAMPTZ,
    LastReadAt TIMESTAMPTZ,
    UNIQUE (ConversationId, UserId)
);

-- Direct Messages Table
Messages (
    Id SERIAL PRIMARY KEY,
    ConversationId INT REFERENCES Conversations(Id) ON DELETE CASCADE,
    SenderUserId INT REFERENCES Users(Id) ON DELETE CASCADE,
    Content TEXT NOT NULL,
    CreatedAt TIMESTAMPTZ,
    UpdatedAt TIMESTAMPTZ,
    IsDeleted BOOLEAN DEFAULT FALSE
);
```

---
*Last Updated: October 2026 | Marbook Project Master Roadmap*
