import {ClientHintGeocoder} from "./client-hint-geocoder.js";
import {NominatimGeocoder} from "./nominatim-geocoder.js";
import {ClientLocationHint, Coordinates, Jurisdiction, ReverseGeocoder} from "./types.js";

function provider(): ReverseGeocoder {
  const name = (process.env.GEOCODING_PROVIDER ?? "client-native").toLowerCase();
  if (name === "client-native") return new ClientHintGeocoder();
  if (name === "nominatim") {
    const url = process.env.NOMINATIM_BASE_URL;
    if (!url) throw new Error("SERVICE_NOT_CONFIGURED");
    return new NominatimGeocoder(url);
  }
  throw new Error("SERVICE_NOT_CONFIGURED");
}

export function reverseGeocode(coordinates: Coordinates, hint?: ClientLocationHint): Promise<Jurisdiction> {
  return provider().reverse(coordinates, hint);
}

export type {Jurisdiction};
