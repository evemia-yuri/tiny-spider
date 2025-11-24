import axios from "axios";
import { writeFile } from "fs/promises";
import crypto from "node:crypto";
import { stringify } from "node:querystring";

const HEADERS = {
  "user-agent":
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) \
         AppleWebKit/537.36 (KHTML, like Gecko) \
         Chrome/142.0.0.0 Safari/537.36 Edg/142.0.0.0",
  "content-type": "application/x-www-form-urlencoded;charset=utf-8",
};
const BASE_URL = "https://dict.youdao.com/jsonapi_s?doctype=json&jsonversion=4";

/**
 * Generate request payload data.
 * @param {string} keyword Target words or sentence.
 * @param {string} target Target translate language.
 * @returns {string} Payload string.
 */
function buildPayload(keyword, lang) {
  const hFunc = (token) => {
    return crypto.createHash("md5").update(token.toString()).digest("hex");
  };
  const keyform = "webdict";
  const time = "".concat(keyword).concat(keyform).length % 10;
  const secret = "Mk6hqtUp33DGGtoS63tTJbMUYjRrG1Lu";

  const r = "".concat(keyword).concat(keyform);
  const o = hFunc(r);
  const n = ""
    .concat("web")
    .concat(keyword)
    .concat(time)
    .concat(secret)
    .concat(o);
  const sign = hFunc(n);

  return stringify({
    q: keyword,
    le: lang,
    t: time,
    client: "web",
    sign: sign,
    keyform: keyform,
  });
}

/**
 * Make a request to the server and return the original json data.
 * @param {string} keyword Target words or sentence.
 * @param {string} target Target translate language.
 * @returns {Promise<object>} Raw json data from server which contains words and
 *     sentences.
 */
async function query(keyword, lang) {
  const wordsPromise = axios.post(BASE_URL, buildPayload(keyword, lang), {
    headers: HEADERS,
  });
  const sentencesPromise = axios.post(
    BASE_URL,
    buildPayload("lj:".concat(keyword), lang),
    { headers: HEADERS }
  );

  try {
    const [wordsReponse, sentencesResponse] = await Promise.all([
      wordsPromise,
      sentencesPromise,
    ]);
    return { words: wordsReponse.data, sentences: sentencesResponse.data };
  } catch (error) {
    console.log(`Error from function 'query()': ${error}`);
    return {};
  }
}

/**
 * Extract the target data from raw json data.
 * @param {object} rawData Raw json data from server.
 * @returns {object} Target data.
 */
function dataExtraction(rawData) {
  /** @type {Array<{'#text': string, '#tran': string}>} */
  const wordsRaw = rawData.words.ce.word.trs;
  const words = wordsRaw.reduce((acc, word) => {
    acc[word["#text"]] = word["#tran"];
    return acc;
  }, {});

  /** @type {Array<{key: string, trans: Array<{'value': string}>}>} */
  const phrasesRaw = rawData.words.web_trans["web-translation"];
  const phrases = phrasesRaw.slice(1).reduce((acc, phrase) => {
    acc[phrase.trans[0].value] = phrase.key;
    return acc;
  }, {});

  /** @type {Array<{sentence: string, 'sentence-translation': string}>} */
  const sentencesRaw = rawData.sentences.blng_sents["sentence-pair"];
  const sentences = sentencesRaw.reduce((acc, sentence) => {
    acc[sentence["sentence-translation"]] = sentence.sentence;
    return acc;
  }, {});

  return { words, phrases, sentences };
}

/**
 * Entry of this project.
 */
async function main() {
  const responseData = await query("火光", "en");

  const fileName = "output.json";
  await writeFile(
    fileName,
    JSON.stringify(dataExtraction(responseData), null, 2)
  );
}

main();
