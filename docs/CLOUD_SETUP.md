# Cloud sync: one-time setup (Firebase)

Cloud sync keeps your projects, blueprints and imported models the same on your PC and phone.
It uses **your own Firebase project** (Google). It's **free** on the Spark plan:

- 1 GB of storage
- 50,000 reads and 20,000 writes per day

One person syncing a few times an hour uses only a small part of that. You do this setup **once**. It takes about 10 minutes.

> Use a **new, separate** Firebase project just for this app.

## 1. Make the project
1. Open https://console.firebase.google.com and sign in with your Google account.
2. Click **Create a project** (or **Add project**).
3. Name: `ay-3d-builder` → **Continue**.
4. Google Analytics: switch it **off** → **Create project** → wait → **Continue**.

## 2. Switch on email sign-in
1. In the left menu: **Build** → **Authentication** → **Get started**.
2. Tab **Sign-in method** → click **Email/Password**.
3. Switch on the **first** switch (Email/Password). Leave "Email link" off → **Save**.

## 3. Make the database
1. Left menu: **Build** → **Firestore Database** → **Create database**.
2. Location: **asia-southeast2 (Jakarta)**. You can't change this later. → **Next**.
3. Choose **Start in production mode** → **Create**.
4. When it's ready, open the **Rules** tab. Delete everything there and paste this:

```
rules_version = '2';
service cloud.firestore {
  match /databases/{database}/documents {
    // Each person can only read and write their own things.
    match /users/{uid}/{document=**} {
      allow read, write: if request.auth != null && request.auth.uid == uid;
    }
  }
}
```

5. Click **Publish**.

## 4. Copy the two values
1. Click the **gear** (next to "Project Overview") → **Project settings**, tab **General**.
2. Copy the **Project ID**, for example `ay-3d-builder`.
3. Copy the **Web API key**.
   - If it says "No Web API key": scroll down to "Your apps" → click the **`</>`** (Web) icon → nickname `3d-builder` → **Register app**.
   - Then go back to Project settings. The Web API key now shows.

## 5. In the app
1. Press **Esc** → **Cloud sync (PC + phone)**.
2. Paste the **Web API key** and the **Project ID** → **Save**.
3. Type your **email** and a **password** (at least 6 characters) → **Create account**.
4. Done. It syncs by itself:
   - at start
   - a few seconds after you save
   - every 5 minutes

   **Sync now** forces it.
5. On another device: do steps 1-2 of this section, then **Sign in** with the same email and password.

## Good to know
- **Without internet** everything still works. It syncs when you're back online.
- **Changed on two devices before syncing?** The newest one wins. The other one is kept as "<name> (conflict copy)", so nothing is lost.
- **Delete on one device** = it moves to the trash folder on the other devices too.
- The API key is **not a secret** (every Firebase web app has one). The **Rules** from step 3 protect your data.
- **Forgot your password?** Type the email → **Forgot password** → a reset email comes from Firebase.
