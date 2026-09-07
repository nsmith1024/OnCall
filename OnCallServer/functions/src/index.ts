/**
 * Import function triggers from their respective submodules:
 *
 * import {onCall} from "firebase-functions/v2/https";
 * import {onDocumentWritten} from "firebase-functions/v2/firestore";
 *
 * See a full list of supported triggers at https://firebase.google.com/docs/functions
 */

//import {setGlobalOptions} from "firebase-functions";
//import {onRequest} from "firebase-functions/https";
//import * as logger from "firebase-functions/logger";

// Start writing functions
// https://firebase.google.com/docs/functions/typescript

// For cost control, you can set the maximum number of containers that can be
// running at the same time. This helps mitigate the impact of unexpected
// traffic spikes by instead downgrading performance. This limit is a
// per-function limit. You can override the limit for each function using the
// `maxInstances` option in the function's options, e.g.
// `onRequest({ maxInstances: 5 }, (req, res) => { ... })`.
// NOTE: setGlobalOptions does not apply to functions using the v1 API. V1
// functions should each use functions.runWith({ maxInstances: 10 }) instead.
// In the v1 API, each function can only serve one request per container, so
// this will be the maximum concurrent request count.
//setGlobalOptions({ maxInstances: 10 });

// export const helloWorld = onRequest((request, response) => {
//   logger.info("Hello logs!", {structuredData: true});
//   response.send("Hello from Firebase!");
// });import { onRequest } from "firebase-functions/v2/https";
import { onRequest } from "firebase-functions/v2/https";
import * as admin from "firebase-admin";

admin.initializeApp();
const db = admin.firestore();

// HTTP endpoint exposed to external clients like .NET MAUI
export const getValue = onRequest({ cors: true }, async (req, res) => {
    try {
        // Read the document created in Step 1
        const docRef = db.collection("system_data").doc("app_config");
        const docSnap = await docRef.get();

        if (!docSnap.exists) {
            res.status(404).json({ error: "Document not found" });
            return;
        }

        const data = docSnap.data();
        // Return the value as JSON
        res.status(200).json({ value: data?.status_message });
    } catch (error) {
        res.status(500).json({ error: (error as Error).message });
    }
});

