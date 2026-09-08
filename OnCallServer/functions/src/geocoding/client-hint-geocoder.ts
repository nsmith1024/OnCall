import {ClientLocationHint, Coordinates, Jurisdiction, ReverseGeocoder} from "./types.js";

export class ClientHintGeocoder implements ReverseGeocoder {
  async reverse(_coordinates: Coordinates, hint?: ClientLocationHint): Promise<Jurisdiction> {
    const city = hint?.city?.trim();
    const state = hint?.state?.trim().toUpperCase();
    if (!city || city.length > 100 || !state || state.length !== 2) throw new Error("INVALID_LOCATION_HINT");
    return {city, state, provider: "client-native", verifiedByServer: false};
  }
}
