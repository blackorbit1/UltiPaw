# UltiPaw

This repository contains a Unity package. The original GitHub pipeline built release packages automatically.

A simple Node.js server script `server.js` provides an endpoint to build the same packages locally. It zips the repository and creates a `.unitypackage` using the meta files.

## Usage

Install dependencies and start the server:

```bash
npm install
npm start
```

Trigger a build by visiting `http://localhost:3000/build`.
