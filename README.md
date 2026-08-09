# RadiopaediaConnect

An open-source uploader for [Radiopaedia](https://radiopaedia.org), the collaborative radiology resource.

RadiopaediaConnect is a self-hosted ASP.NET Core web application that integrates with a hospital PACS (Picture Archiving and Communication System) and the Radiopaedia teaching-file platform. It accepts DICOM studies via a built-in C-STORE SCP listener, allows radiologists to select and submit cases to Radiopaedia, and manages the OAuth2 authentication flow against the Radiopaedia API.

## Why open source?

Radiopaedia is built by the radiology community, and we want to make contributing cases as easy as possible. By publishing this code we hope to:

- provide a working reference implementation for integrating with the Radiopaedia API
- make it straightforward for radiology departments to set up their own uploader
- encourage PACS vendors (and anyone else) to build Radiopaedia upload support into their products

## Features

- Free: RadiopaediaConnect is 100% free
- License: open source, Apache 2.0 license
- Client side anonimisation: no patient information leaves your institution, everything is done on your end before uploading begins
- Redacting: blacking out areas of images before uploading
- Reduce image number: easily trim large stacks or select to upload only every second or third image
- Fast: uploading cases using RadiopaediaConnect is much faster than using the browser, allowing you to collect cases while reporting
- Draft submission: cases are submitted as drafts, giving you full control before publishing
- Case sync: My Cases checks your Radiopaedia case list, so you can see which of your uploads are still drafts, which have been published, and which no longer exist on Radiopaedia
- Safe additions: extra imaging can only be added to a case while it is still a draft, checked with Radiopaedia before anything is uploaded

## Getting Started

Installation instructions and links to the docker container are available at [https://radiopaedia.org/radiopaediaconnect](https://radiopaedia.org/radiopaediaconnect)

## API Documentation

For details on the Radiopaedia API, see [radiopaedia.org/api-documentation](https://radiopaedia.org/api-documentation).

## Licence

This project is licensed under the [Apache License 2.0](LICENSE).

### Important Notice

This licence applies to the RadiopaediaConnect source code only. The Radiopaedia API, the Radiopaedia platform, and all content hosted on Radiopaedia (including user-contributed cases, articles, and images) remain subject to the [Radiopaedia Terms of Use](https://radiopaedia.org/terms). Use of the Radiopaedia API requires a valid Radiopaedia account and compliance with those terms.

The Radiopaedia name, logo, and trademarks are the property of Radiopaedia Australia Pty Ltd and may not be used without prior written permission, except as required for reasonable and customary use in describing the origin of this software.

## Contributing

We welcome contributions. Please open an issue or submit a pull request.

## Contact

- Email: [general@radiopaedia.org](mailto:general@radiopaedia.org)
- Forum: [Radiopaedia Chat](https://radiopaedia.org/chat/main/channels/radiopaediaconnect) - you need to log in with your Radiopaedia account
